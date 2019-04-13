﻿using Engine.Model.Common.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;

namespace Engine.Model.Common.Entities
{
  [Serializable]
  public class Room : IEquatable<Room>, IDisposable
  {
    /// <summary>
    /// Identifier which mark messages that can't be edited.
    /// </summary>
    public const long SpecificMessageId = -1;

    protected readonly string _name;
    protected readonly HashSet<UserId> _users;
    protected readonly Dictionary<long, Message> _messages;
    protected readonly Dictionary<FileId, FileDescription> _files;

    protected UserId _admin;

    private long _lastMessageId;
    private bool _enabled;

    /// <summary>
    /// Create the room.
    /// </summary>
    /// <param name="admin">User id which be administrator of the room.</param>
    /// <param name="name">Room name.</param>
    [SecuritySafeCritical]
    public Room(UserId admin, string name)
    {
      _admin = admin;
      _name = name;
      _users = new HashSet<UserId>();
      _messages = new Dictionary<long, Message>();
      _files = new Dictionary<FileId, FileDescription>();

      if (admin != UserId.Empty)
        _users.Add(admin);
    }

    /// <summary>
    /// Create the room.
    /// </summary>
    /// <param name="admin">User id which be administrator of the room.</param>
    /// <param name="name">Room name.</param>
    /// <param name="initialUsers">Initial room users list.</param>
    [SecuritySafeCritical]
    public Room(UserId admin, string name, IEnumerable<User> initialUsers)
      : this(admin, name)
    {
      foreach (var nick in initialUsers.Select(u => u.Id))
        _users.Add(nick);
    }

    /// <summary>
    /// Room name.
    /// </summary>
    public string Name
    {
      [SecuritySafeCritical]
      get { return _name; }
    }

    /// <summary>
    /// Administrator user id.
    /// </summary>
    public UserId Admin
    {
      [SecuritySafeCritical]
      get { return _admin; }
      [SecuritySafeCritical]
      set { _admin = value; }
    }

    #region enable/disable
    /// <summary>
    /// Is room enabled.
    /// </summary>
    public bool Enabled
    {
      [SecuritySafeCritical]
      get { return _enabled; }
    }

    /// <summary>
    /// Enable room.
    /// </summary>
    [SecuritySafeCritical]
    public virtual void Enable()
    {
      _enabled = true;
    }

    /// <summary>
    /// Disable room.
    /// </summary>
    [SecuritySafeCritical]
    public virtual void Disable()
    {
      _enabled = false;
    }
    #endregion

    #region users
    /// <summary>
    /// Returns true if room is empty, otherwise false.
    /// </summary>
    public bool IsEmpty
    {
      [SecuritySafeCritical]
      get { return _users.Count == 0; }
    }

    /// <summary>
    /// Users collection, including administrator.
    /// </summary>
    public IEnumerable<UserId> Users
    {
      [SecuritySafeCritical]
      get { return _users; }
    }

    /// <summary>
    /// Returns true if user with this nick exist in room, otherwise false.
    /// </summary>
    /// <param name="userId">User id.</param>
    [SecuritySafeCritical]
    public bool IsUserExist(UserId userId)
    {
      return _users.Contains(userId);
    }

    /// <summary>
    /// Add user to room.
    /// </summary>
    /// <param name="userId">User id.</param>
    [SecuritySafeCritical]
    public virtual void AddUser(UserId userId)
    {
      if (_users.Contains(userId))
        throw new ArgumentException("User already exist.");

      _users.Add(userId);
    }

    /// <summary>
    /// Remove user from room, including all his files.
    /// </summary>
    /// <param name="userId">User id.</param>
    [SecuritySafeCritical]
    public virtual void RemoveUser(UserId userId)
    {
      _users.Remove(userId);

      // Remove all files
      var removingFiles = new HashSet<FileId>();
      foreach (var fileId in _files.Keys)
      {
        if (fileId.Owner == userId)
          removingFiles.Add(fileId);
      }

      foreach (var fileId in removingFiles)
        _files.Remove(fileId);
    }
    #endregion

    #region messages
    /// <summary>
    /// Returns true if message with this id exist in room.
    /// </summary>
    /// <param name="messageId">Message id.</param>
    [SecuritySafeCritical]
    public bool IsMessageExist(long messageId)
    {
      return _messages.ContainsKey(messageId);
    }

    /// <summary>
    /// Messages collection.
    /// </summary>
    public IEnumerable<Message> Messages
    {
      [SecuritySafeCritical]
      get { return _messages.Values; }
    }

    /// <summary>
    /// Add message to room.
    /// </summary>
    /// <param name="ownerId">User id which is message owner.</param>
    /// <param name="text">Message text.</param>
    /// <returns>Added message.</returns>
    [SecuritySafeCritical]
    public Message AddMessage(UserId ownerId, string text)
    {
      var message = AddMessage(_lastMessageId, ownerId, text);
      if (message.Id == _lastMessageId)
        _lastMessageId++;

      return message;
    }

    /// <summary>
    /// Add message to room.
    /// </summary>
    /// <param name="messageId">Message id. If message with this id already exist then added text be contacted to him.</param>
    /// <param name="ownerId">User id which is message owner.</param>
    /// <param name="text">Message text.</param>
    /// <returns>Added message.</returns>
    [SecuritySafeCritical]
    public Message AddMessage(long messageId, UserId ownerId, string text)
    {
      var message = new Message(messageId, ownerId, text);
      var lastMessage = GetMessage(_lastMessageId - 1);

      if (lastMessage != null && lastMessage.TryConcat(message))
        return lastMessage;

      _messages[message.Id] = message;
      return message;
    }

    /// <summary>
    /// Add message to room.
    /// </summary>
    /// <param name="message">Message which will be added. If message already exist then it will be replaced.</param>
    [SecuritySafeCritical]
    public void AddMessage(Message message)
    {
      _messages[message.Id] = message;
    }

    /// <summary>
    /// Returns the message if it exist, otherwise it returns null.
    /// </summary>
    /// <param name="messageId">Message id.</param>
    /// <returns>Message.</returns>
    [SecuritySafeCritical]
    public Message GetMessage(long messageId)
    {
      _messages.TryGetValue(messageId, out var message);
      return message;
    }

    /// <summary>
    /// Returns true if message belong to user, otherwise false.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="messageId">Message that be checked.</param>
    [SecuritySafeCritical]
    public bool IsMessageBelongToUser(UserId userId, long messageId)
    {
      var message = GetMessage(messageId);
      if (message == null)
        return false;

      return userId == message.Owner;
    }

    /// <summary>
    /// Remove message from room.
    /// </summary>
    /// <param name="messageId">Message that be checked.</param>
    /// <returns>Removed messages.</returns>
    [SecuritySafeCritical]
    public Message RemoveMessage(long messageId)
    {
      if (_messages.TryGetValue(messageId, out var message))
        _messages.Remove(messageId);
      return message;
    }

    /// <summary>
    /// Remove messages from room.
    /// </summary>
    /// <param name="messageIds">Message ids that be removed from room.</param>
    [SecuritySafeCritical]
    public void RemoveMessages(IEnumerable<long> messageIds)
    {
      foreach(var messageId in messageIds)
        _messages.Remove(messageId);
    }
    #endregion

    #region files
    /// <summary>
    /// Files collection.
    /// </summary>
    public IEnumerable<FileDescription> Files
    {
      [SecuritySafeCritical]
      get { return _files.Values; }
    }

    /// <summary>
    /// Check is file present in room.
    /// </summary>
    /// <param name="fileId"></param>
    /// <returns>Returns true if file exist, otherwise false.</returns>
    [SecuritySafeCritical]
    public bool IsFileExist(FileId fileId)
    {
      return _files.ContainsKey(fileId);
    }

    /// <summary>
    /// Get file from room.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <returns>Returns FileDescription if it exist, otherwise null.</returns>
    [SecuritySafeCritical]
    public FileDescription TryGetFile(FileId fileId)
    {
      _files.TryGetValue(fileId, out var file);
      return file;
    }

    /// <summary>
    /// Add file to room.
    /// </summary>
    /// <param name="file">File description.</param>
    [SecuritySafeCritical]
    public void AddFile(FileDescription file)
    {
      if (_files.ContainsKey(file.Id))
        throw new ArgumentException("File already exist.");
      _files.Add(file.Id, file);
    }

    /// <summary>
    /// Remove file from room.
    /// </summary>
    /// <param name="file">File identifier.</param>
    [SecuritySafeCritical]
    public virtual bool RemoveFile(FileId fileId)
    {
      return _files.Remove(fileId);
    }
    #endregion

    #region toDto
    [SecuritySafeCritical]
    public virtual RoomDto ToDto(UserId dtoReciver)
    {
      return new RoomDto(_name, _admin, _users, _files.Values, _messages.Values, RoomType.Chat, null);
    }
    #endregion

    #region equals
    [SecuritySafeCritical]
    public override bool Equals(object obj)
    {
      if (obj is null)
        return false;

      if (ReferenceEquals(obj, this))
        return true;

      var room = obj as Room;
      if (room is null)
        return false;

      return Equals(room);
    }

    [SecuritySafeCritical]
    public override int GetHashCode()
    {
      return _name.GetHashCode();
    }

    [SecuritySafeCritical]
    public bool Equals(Room room)
    {
      if (room is null)
        return false;

      if (ReferenceEquals(room, this))
        return true;

      return string.Equals(_name, room._name);
    }
    #endregion

    #region dispose
    [SecuritySafeCritical]
    protected virtual void ReleaseManagedResources()
    {

    }

    [SecuritySafeCritical]
    public void Dispose()
    {
      ReleaseManagedResources();
    }
    #endregion
  }
}
