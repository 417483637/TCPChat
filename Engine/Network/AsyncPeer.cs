﻿using Engine.Api;
using Engine.Api.Client.P2P;
using Engine.Helpers;
using Engine.Model.Client;
using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Security.Cryptography;
using System.Threading;

namespace Engine.Network
{
  public enum PeerState
  {
    NotConnected = 0,
    ConnectedToService = 1,
    ConnectedToPeers = 2,
  }
  
  public class AsyncPeer :
    MarshalByRefObject,
    IDisposable
  {
    #region nested types
    private class WaitingCommandContainer
    {
      public IPackage Package { get; private set; }
      public byte[] RawData { get; private set; }
      public bool Unreliable { get; private set; }

      public WaitingCommandContainer(IPackage package, byte[] rawData, bool unreliable)
      {
        Package = package;
        RawData = rawData;
        Unreliable = unreliable;
      }
    }
    #endregion

    #region consts
    public const string NetConfigString = "Peer TCPChat";
    private const int KeySize = 256;
    #endregion

    #region private fields
    [SecurityCritical] private readonly object _syncObject = new object();
    [SecurityCritical] private readonly Dictionary<string, List<WaitingCommandContainer>> _waitingCommands;
    [SecurityCritical] private readonly Dictionary<string, Packer> _packers;
    [SecurityCritical] private readonly SynchronizationContext _syncContext;
    [SecurityCritical] private readonly RequestQueue _requestQueue;
    [SecurityCritical] private readonly IApi _api;
    [SecurityCritical] private readonly ECDiffieHellmanCng _diffieHellman;
    [SecurityCritical] private readonly IClientNotifier _notifier;
    [SecurityCritical] private readonly Logger _logger;

    [SecurityCritical] private NetConnection _serviceConnection;
    [SecurityCritical] private NetPeer _handler;

    [SecurityCritical] private int _state; //PeerState
    [SecurityCritical] private bool _disposed;
    #endregion

    #region events and properties
    /// <summary>
    /// Состояние пира.
    /// </summary>
    public PeerState State
    {
      [SecuritySafeCritical]
      get { return (PeerState)_state; }
    }
    #endregion

    #region constructor
    [SecurityCritical]
    internal AsyncPeer(IApi api, IClientNotifier notifier, Logger logger)
    {
      _waitingCommands = new Dictionary<string, List<WaitingCommandContainer>>();
      _packers = new Dictionary<string, Packer>();
      _syncContext = new EngineSyncContext();
      _requestQueue = new RequestQueue(api);
      _api = api;
      _diffieHellman = new ECDiffieHellmanCng(KeySize);
      _diffieHellman.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash;
      _diffieHellman.HashAlgorithm = CngAlgorithm.Sha256;
      _notifier = notifier;
      _logger = logger;
    }
    #endregion

    #region public methods
    /// <summary>
    /// Connects to P2P service, for create UDP window.
    /// </summary>
    /// <param name="remotePoint">Service address.</param>
    [SecurityCritical]
    internal void ConnectToService(IPEndPoint remotePoint)
    {
      ThrowIfDisposed();

      if (Interlocked.CompareExchange(ref _state, (int)PeerState.ConnectedToService, (int)PeerState.NotConnected) != (int)PeerState.NotConnected)
        throw new InvalidOperationException("Peer has not right state.");

      if (_handler != null && _handler.Status == NetPeerStatus.Running)
        throw new ArgumentException("Already runned.");

      var config = new NetPeerConfiguration(NetConfigString);
      config.Port = 0;
      config.AcceptIncomingConnections = true;
      config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

      if (remotePoint.AddressFamily == AddressFamily.InterNetworkV6)
        config.LocalAddress = IPAddress.IPv6Any;

      _handler = new NetPeer(config);
      _syncContext.Send(RegisterReceived, _handler);
      _handler.Start();

      var hail = _handler.CreateMessage();
      using (var client = ClientModel.Get())
      {
        var localPoint = new IPEndPoint(Connection.GetIpAddress(remotePoint.AddressFamily), _handler.Port);

        hail.Write(client.Chat.User.Nick);
        hail.Write(localPoint);
      }

      _serviceConnection = _handler.Connect(remotePoint, hail);

      _logger.WriteDebug("AsyncPeer.ConnectToService({0})", remotePoint);
    }

    [SecurityCritical]
    private void RegisterReceived(object obj)
    {
      var server = (NetPeer)obj;
      server.RegisterReceivedCallback(OnReceive);
    }

    /// <summary>
    /// Starts waiting for connection from other client.
    /// <remarks>Can be called only after service connection.</remarks>
    /// </summary>
    /// <param name="waitingPoint">End point where from other client will be connecting.</param>
    [SecurityCritical]
    internal void WaitConnection(IPEndPoint waitingPoint)
    {
      ThrowIfDisposed();

      int oldState = Interlocked.CompareExchange(ref _state, (int)PeerState.ConnectedToPeers, (int)PeerState.ConnectedToService);
      if (oldState == (int)PeerState.NotConnected)
        throw new InvalidOperationException("Peer has not right state.");

      // Creating and sending message that be pierce NAT 
      // and creates possibility accept incoming message
      var holePunchMessage = _handler.CreateMessage();
      holePunchMessage.Write((byte)0);
      _handler.SendUnconnectedMessage(holePunchMessage, waitingPoint);

      DisconnectFromService();

      _logger.WriteDebug("AsyncPeer.WaitConnection({0})", waitingPoint);
    }

    /// <summary>
    /// Connects to other peer.
    /// <remarks>Can be called only after service connection.</remarks>
    /// </summary>
    /// <param name="peerId">Peer id.</param>
    /// <param name="remotePoint">Peer address.</param>
    [SecurityCritical]
    internal void ConnectToPeer(string peerId, IPEndPoint remotePoint)
    {
      ThrowIfDisposed();

      int oldState = Interlocked.CompareExchange(ref _state, (int)PeerState.ConnectedToPeers, (int)PeerState.ConnectedToService);
      if (oldState == (int)PeerState.NotConnected)
        throw new InvalidOperationException("Peer has not right state.");

      if (_handler == null)
        throw new InvalidOperationException("Handler not created.");

      _handler.Connect(remotePoint, CreateHailMessage());
      
      DisconnectFromService();

      _logger.WriteDebug("AsyncPeer.ConnectToPeer({0}, {1})", peerId, remotePoint);
    }

    /// <summary>
    /// Returns true if peer already connected.
    /// </summary>
    /// <param name="peerId">Peer id.</param>
    [SecuritySafeCritical]
    public bool IsConnected(string peerId)
    {
      ThrowIfDisposed();

      if (_handler == null || _handler.Status != NetPeerStatus.Running)
        return false;

      return FindConnection(peerId) != null;
    }

    #region Send message
    /// <summary>
    /// Sends command. <b>If connection not exist, then it will be created.</b>
    /// </summary>
    /// <param name="peerId">Peer id that will be receive message.</param>
    /// <param name="id">Command id.</param>
    /// <param name="content">Command content.</param>
    /// <param name="unreliable">Send unreliable message. (fastest)</param>
    [SecuritySafeCritical]
    public void SendMessage<T>(string peerId, long id, T content, bool unreliable = false)
    {
      SendMessage(peerId, new Package<T>(id, content), null, unreliable);
    }

    /// <summary>
    /// Sends command. <b>If connection not exist, then it will be created.</b>
    /// </summary>
    /// <param name="peerId">Peer id that will be receive message.</param>
    /// <param name="id">Command id.</param>
    /// <param name="content">Command content.</param>
    /// <param name="rawData">Data that not be serialized.</param>
    /// <param name="unreliable">Send unreliable message. (fastest)</param>
    [SecuritySafeCritical]
    public void SendMessage<T>(string peerId, long id, T content, byte[] rawData, bool unreliable = false)
    {
      SendMessage(peerId, new Package<T>(id, content), rawData, unreliable);
    }

    /// <summary>
    /// Sends command. <b>If connection not exist, then it will be created.</b>
    /// </summary>
    /// <param name="peerId">Peer id that will be receive message.</param>
    /// <param name="id">Command id.</param>
    /// <param name="unreliable">Send unreliable message. (fastest)</param>
    [SecuritySafeCritical]
    public void SendMessage(string peerId, long id, bool unreliable = false)
    {
      SendMessage(peerId, new Package(id), null, unreliable);
    }

    /// <summary>
    /// Sends command. <b>If connection not exist, then it will be created.</b>
    /// </summary>
    /// <param name="peerId">Peer id that will be receive message.</param>
    /// <param name="package">Package to send.</param>
    /// <param name="rawData">Data that not be serialized.</param>
    /// <param name="unreliable">Send unreliable message. (fastest)</param>
    [SecuritySafeCritical]
    public void SendMessage(string peerId, IPackage package, byte[] rawData, bool unreliable = false)
    {
      ThrowIfDisposed();

      if (_handler == null || _handler.Status != NetPeerStatus.Running)
      {
        SaveCommandAndConnect(peerId, package, rawData, unreliable);
        return;
      }

      var connection = FindConnection(peerId);
      if (connection == null)
      {
        SaveCommandAndConnect(peerId, package, rawData, unreliable);
        return;
      }

      var deliveryMethod = unreliable ? NetDeliveryMethod.Unreliable : NetDeliveryMethod.ReliableOrdered;
      var message = CreateMessage(peerId, package, rawData);
      _handler.SendMessage(message, connection, deliveryMethod, 0);
    }
    #endregion

    #region Send message if connected
    /// <summary>
    /// Sends command. <b>If connection not exist, then message will be skipped.</b>
    /// </summary>
    /// <param name="peerId">Peer id that will be receive message.</param>
    /// <param name="id">Command id.</param>
    /// <param name="content">Command content.</param>
    /// <param name="unreliable">Send unreliable message. (fastest)</param>
    /// <returns>Returns true if command was sent.</returns>
    [SecuritySafeCritical]
    public bool SendMessageIfConnected<T>(string peerId, long id, T content, bool unreliable = false)
    {
      return SendMessageIfConnected(peerId, new Package<T>(id, content), null, unreliable);
    }

    /// <summary>
    /// Sends command. <b>If connection not exist, then message will be skipped.</b>
    /// </summary>
    /// <param name="peerId">Peer id that will be receive message.</param>
    /// <param name="package">Индетификатор пакета.</param>
    /// <param name="rawData">Данные не требующие сериализации.</param>
    /// <param name="unreliable">Send unreliable message. (fastest)</param>
    /// <returns>Returns true if command was sent.</returns>
    [SecuritySafeCritical]
    public bool SendMessageIfConnected(string peerId, IPackage package, byte[] rawData, bool unreliable = false)
    {
      ThrowIfDisposed();

      if (_handler == null || _handler.Status != NetPeerStatus.Running)
        return false;

      var connection = FindConnection(peerId);
      if (connection == null)
        return false;

      var deliveryMethod = unreliable ? NetDeliveryMethod.Unreliable : NetDeliveryMethod.ReliableOrdered;
      var message = CreateMessage(peerId, package, rawData);
      _handler.SendMessage(message, connection, deliveryMethod, 0);
      return true;
    }
    #endregion
    #endregion

    #region private methods
    [SecurityCritical]
    private Packer GetPacker(string peerId)
    {
      Packer packer;
      lock (_syncObject)
        _packers.TryGetValue(peerId, out packer);

      if (packer == null)
        throw new InvalidOperationException(string.Format("Packer not set, for connection {0}", peerId));

      return packer;
    }

    [SecurityCritical]
    private NetOutgoingMessage CreateMessage(string peerId, IPackage package, byte[] rawData)
    {
      var packer = GetPacker(peerId);
      var packed = packer.Pack(package, rawData);
      
      var message = _handler.CreateMessage(packed.Length);
      message.Write(packed.Data, 0, packed.Length);
      return message;
    }

    [SecurityCritical]
    private NetOutgoingMessage CreateHailMessage()
    {
      string nick;
      using (var client = ClientModel.Get())
        nick = client.Chat.User.Nick;

      var publicKeyBlob = _diffieHellman.PublicKey.ToByteArray();
      if (publicKeyBlob == null)
        throw new InvalidOperationException("public key is null");

      var hailMessage = _handler.CreateMessage();
      hailMessage.Write(nick);
      hailMessage.Write(publicKeyBlob.Length);
      hailMessage.Write(publicKeyBlob);

      return hailMessage;
    }

    [SecurityCritical]
    private void SaveCommandAndConnect(string peerId, IPackage package, byte[] rawData, bool unreliable)
    {
      lock (_syncObject)
      {
        List<WaitingCommandContainer> commands;

        if (!_waitingCommands.TryGetValue(peerId, out commands))
        {
          commands = new List<WaitingCommandContainer>();
          _waitingCommands.Add(peerId, commands);
        }

        commands.Add(new WaitingCommandContainer(package, rawData, unreliable));
      }

      _api.Perform(new ClientConnectToPeerAction(peerId));
    }

    [SecurityCritical]
    private void DisconnectFromService()
    {
      if (_serviceConnection != null)
      {
        _serviceConnection.Disconnect(string.Empty);
        _serviceConnection = null;
      }
    }

    [SecurityCritical]
    private NetConnection FindConnection(string id)
    {
      return _handler.Connections.SingleOrDefault(new Finder(id).Equals);
    }

    private class Finder
    {
      private readonly string _id;

      public Finder(string id)
      {
        _id = id;
      }

      [SecurityCritical]
      public bool Equals(NetConnection connection)
      {
        return string.Equals((string)connection.Tag, _id);
      }
    }
    #endregion

    #region callback method
    [SecurityCritical]
    private void OnReceive(object obj)
    {
      if (_handler == null || _handler.Status != NetPeerStatus.Running)
        return;

      NetIncomingMessage message;

      while ((message = _handler.ReadMessage()) != null)
      {
        switch (message.MessageType)
        {
          case NetIncomingMessageType.ErrorMessage:
          case NetIncomingMessageType.WarningMessage:
            var error = new NetException(message.ReadString());
            _notifier.AsyncError(new AsyncErrorEventArgs(error));
            break;

          case NetIncomingMessageType.ConnectionApproval:
            OnApprove(message);
            break;

          case NetIncomingMessageType.StatusChanged:
            NetConnectionStatus status = (NetConnectionStatus)message.ReadByte();

            if (status == NetConnectionStatus.Connected && _state == (int)PeerState.ConnectedToPeers)
              OnPeerConnected(message);

            if (status == NetConnectionStatus.Connected && _state == (int)PeerState.ConnectedToService)
              OnServiceConnected(message);

            if (status == NetConnectionStatus.Disconnecting || status == NetConnectionStatus.Disconnected)
              OnDisconnected(message);
            break;

          case NetIncomingMessageType.Data:
          case NetIncomingMessageType.UnconnectedData:
            if (_state == (int)PeerState.ConnectedToPeers)
              OnPackageReceived(message);
            break;
        }
      }
    }

    [SecurityCritical]
    private void OnApprove(NetIncomingMessage message)
    {
      message.SenderConnection.Approve(CreateHailMessage());
      _logger.WriteDebug("AsyncPeer.Approve()");
    }

    [SecurityCritical]
    private void OnServiceConnected(NetIncomingMessage message)
    {
      _logger.WriteDebug("AsyncPeer.ServiceConnect()");
    }

    [SecurityCritical]
    private void OnPeerConnected(NetIncomingMessage message)
    {
      var hailMessage = message.SenderConnection.RemoteHailMessage;
      if (hailMessage == null)
      {
        message.SenderConnection.Deny();
        _logger.WriteWarning("ConnectionId is null [Message: {0}, SenderEndPoint: {1}]", message.ToString(), message.SenderEndPoint);
        return;
      }

      var connectionId = hailMessage.ReadString();
      var publicKeySize = hailMessage.ReadInt32();
      var publicKeyBlob = hailMessage.ReadBytes(publicKeySize);
      var publicKey = CngKey.Import(publicKeyBlob, CngKeyBlobFormat.EccPublicBlob);
      var key = _diffieHellman.DeriveKeyMaterial(publicKey);

      message.SenderConnection.Tag = connectionId;

      lock (_syncObject)
      {
        // Add connection packer
        var packer = new Packer();
        packer.SetKey(key);
        _packers.Add(connectionId, packer);

        // Invoke waiting commands
        List<WaitingCommandContainer> commands;
        if (_waitingCommands.TryGetValue(connectionId, out commands))
        {
          foreach (var command in commands)
            SendMessage(connectionId, command.Package, command.RawData, command.Unreliable);

          _waitingCommands.Remove(connectionId);
        }
      }

      _logger.WriteDebug("AsyncPeer.PeerConnected({0})", connectionId);
    }

    [SecurityCritical]
    private void OnDisconnected(NetIncomingMessage message)
    {
      var connectionId = (string) message.SenderConnection.Tag;
      message.SenderConnection.Tag = null;

      if (connectionId != null)
        _packers.Remove(connectionId);
    }

    [SecurityCritical]
    private void OnPackageReceived(NetIncomingMessage message)
    {
      try
      {
        var peerId = (string)message.SenderConnection.Tag;
        var packer = GetPacker(peerId);
        var unpacked = packer.Unpack<IPackage>(message.Data);

        _requestQueue.Add(peerId, unpacked);
      }
      catch (Exception exc)
      {
        _notifier.AsyncError(new AsyncErrorEventArgs(exc));
        _logger.Write(exc);
      }
    }
    #endregion

    #region IDisposable
    [SecurityCritical]
    private void ThrowIfDisposed()
    {
      if (_disposed)
        throw new ObjectDisposedException("Object disposed");
    }

    [SecuritySafeCritical]
    public void Dispose()
    {
      if (_disposed)
        return;

      _disposed = true;

      if (_requestQueue != null)
        _requestQueue.Dispose();

      if (_handler != null)
        _handler.Shutdown(string.Empty);

      if (_diffieHellman != null)
        _diffieHellman.Dispose();

      lock (_syncObject)
      {
        _waitingCommands.Clear();
        _packers.Clear();
      }
    }
    #endregion
  }
}
