﻿using Engine.Model.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;

namespace Engine.Model.Client
{
  [SecuritySafeCritical]
  public class ClientNotifier : Notifier
  {
    [SecuritySafeCritical]
    public override IEnumerable<object> GetEvents()
    {
      var events = base.GetEvents();
      return events.Concat(ClientModel.Plugins.GetNotifierEvents());
    }
  }

  [Notifier(typeof(IClientEvents), BaseNotifier = typeof(ClientNotifier))]
  public interface IClientNotifier : INotifier
  {
    void Connected(ConnectEventArgs args);
    void ReceiveRegistrationResponse(RegistrationEventArgs args);

    void ReceiveMessage(ReceiveMessageEventArgs args);

    void AsyncError(AsyncErrorEventArgs args);

    void RoomRefreshed(RoomRefreshedEventArgs args);
    void RoomOpened(RoomOpenedEventArgs args);
    void RoomClosed(RoomClosedEventArgs args);

    void DownloadProgress(FileDownloadEventArgs args);
    void PostedFileDeleted(FileDownloadEventArgs args);

    void PluginLoaded(PluginEventArgs args);
    void PluginUnloading(PluginEventArgs args);

    void TrustedCertificatesChanged(TrustedCertificatesEventArgs args);
  }

  // TODO: rus
  public interface IClientEvents
  {
    /// <summary>
    /// Событие происходит при подключении клиента к серверу.
    /// </summary>
    event EventHandler<ConnectEventArgs> Connected;

    /// <summary>
    /// Событие происходит при полученни ответа от сервера, о регистрации.
    /// </summary>
    event EventHandler<RegistrationEventArgs> ReceiveRegistrationResponse;

    /// <summary>
    /// Событие происходит при полученнии сообщения от сервера.
    /// </summary>
    event EventHandler<ReceiveMessageEventArgs> ReceiveMessage;

    /// <summary>
    /// Событие происходит при любой асинхронной ошибке.
    /// </summary>
    event EventHandler<AsyncErrorEventArgs> AsyncError;

    /// <summary>
    /// Событие происходит при обновлении списка подключенных к серверу клиентов.
    /// </summary>
    event EventHandler<RoomRefreshedEventArgs> RoomRefreshed;

    /// <summary>
    /// Событие происходит при открытии комнаты клиентом. Или когда клиента пригласили в комнату.
    /// </summary>
    event EventHandler<RoomOpenedEventArgs> RoomOpened;

    /// <summary>
    /// Событие происходит при закрытии комнаты клиентом, когда клиента кикают из комнаты.
    /// </summary>
    event EventHandler<RoomClosedEventArgs> RoomClosed;

    /// <summary>
    /// Событие происходит при получении части файла, а также при завершении загрузки файла.
    /// </summary>
    event EventHandler<FileDownloadEventArgs> DownloadProgress;

    /// <summary>
    /// Происходит при удалении выложенного файла.
    /// </summary>
    event EventHandler<FileDownloadEventArgs> PostedFileDeleted;

    /// <summary>
    /// Происходит после успешной загрзуки плагина.
    /// </summary>
    event EventHandler<PluginEventArgs> PluginLoaded;

    /// <summary>
    /// Происходит перед выгрузкой плагина.
    /// </summary>
    event EventHandler<PluginEventArgs> PluginUnloading;

    /// <summary>
    /// Calls when trusted certitifcates storages was changed.
    /// </summary>
    event EventHandler<TrustedCertificatesEventArgs> TrustedCertificatesChanged;
  }
}
