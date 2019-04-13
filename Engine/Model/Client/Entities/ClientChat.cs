﻿using Engine.Model.Common.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace Engine.Model.Client.Entities
{
  [Serializable]
  public class ClientChat : Chat<ClientUser, ClientRoom, ClientVoiceRoom>
  {
    [NonSerialized]
    private Random _idCreator;

    private ClientUser _user;
    private Dictionary<FileId, DownloadingFile> _downloadingFiles;
    private Dictionary<FileId, PostedFile> _postedFiles;

    [SecurityCritical]
    public ClientChat(ClientUser user)
    {
      _idCreator = new Random();

      _user = user;
      _downloadingFiles = new Dictionary<FileId, DownloadingFile>();
      _postedFiles = new Dictionary<FileId, PostedFile>();
    }

    #region user
    /// <summary>
    /// Current user.
    /// </summary>
    public ClientUser User
    {
      [SecuritySafeCritical]
      get { return _user; }
    }
    #endregion

    #region files
    /// <summary>
    /// Check file is downloading.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <returns>Returns true if file downloading, otherwise false.</returns>
    [SecuritySafeCritical]
    public bool IsFileDownloading(FileId fileId)
    {
      return _downloadingFiles.ContainsKey(fileId);
    }

    /// <summary>
    /// Get file from room if it exist.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <returns>Returns DownloadingFile if it exist, otherwise null.</returns>
    [SecuritySafeCritical]
    public DownloadingFile TryGetFileDownload(FileId fileId)
    {
      _downloadingFiles.TryGetValue(fileId, out var file);
      return file;
    }

    /// <summary>
    /// Add file downloading.
    /// </summary>
    /// <param name="file">Downloading file.</param>
    [SecuritySafeCritical]
    public void AddFileDownload(DownloadingFile file)
    {
      _downloadingFiles.Add(file.File.Id, file);
    }

    /// <summary>
    /// Remove and dispose downloading file.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <returns>Returns full file name.</returns>
    [SecuritySafeCritical]
    public string RemoveFileDownload(FileId fileId)
    {
      if (!_downloadingFiles.TryGetValue(fileId, out var file))
        throw new ArgumentException("File not downloading.");

      _downloadingFiles.Remove(fileId);
      var filePath = file.FullName;
      file.Dispose();
      return filePath;
    }

    /// <summary>
    /// Cancel file downloading.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <param name="leaveLoadedPart">If value is true then downloaded file will remained on disc otherwise it will be deleted.</param>
    [SecuritySafeCritical]
    public void CancelFileDownload(FileId fileId, bool leaveLoadedPart = true)
    {
      var filePath = RemoveFileDownload(fileId);
      if (File.Exists(filePath) && !leaveLoadedPart)
        File.Delete(filePath);
    }

    /// <summary>
    /// Return all posted files.
    /// </summary>
    public IEnumerable<PostedFile> PostedFiles
    {
      [SecuritySafeCritical]
      get { return _postedFiles.Values; }
    }

    /// <summary>
    /// Returns posted file.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <returns>Returns PostedFile if it exist, otherwise null.</returns>
    [SecuritySafeCritical]
    public PostedFile TryGetPostedFile(FileId fileId)
    {
      _postedFiles.TryGetValue(fileId, out var posted);
      return posted;
    }

    /// <summary>
    /// If file already posted, then it returns, otherwise is creates and returns.
    /// </summary>
    /// <param name="fullName">Full file name.</param>
    /// <param name="roomName">Room where file posing.</param>
    /// <returns>Returns posted file.</returns>
    [SecuritySafeCritical]
    public PostedFile GetOrCreatePostedFile(FileInfo info, string roomName)
    {
      var posted = _postedFiles.Values.FirstOrDefault(p => p.File.Name == info.Name);
      if (posted == null)
      {
        // Create new file.
        FileId id;
        while (true)
        {
          id = new FileId(_idCreator.Next(int.MinValue, int.MaxValue), _user.Id);
          if (!_postedFiles.ContainsKey(id))
            break;
        }
        var file = new FileDescription(id, info.Length, Path.GetFileName(info.Name));
        posted = new PostedFile(file, info.FullName);
        _postedFiles.Add(posted.File.Id, posted);
      }

      posted.RoomNames.Add(roomName);
      return posted;
    }

    /// <summary>
    /// Remove posted file.
    /// </summary>
    /// <param name="roomName">Room where file was posted.</param>
    /// <param name="fileId">File identifier.</param>
    [SecuritySafeCritical]
    public void RemovePostedFile(string roomName, FileId fileId)
    {
      if (!_postedFiles.TryGetValue(fileId, out var posted))
        throw new InvalidOperationException("Posted file not found");

      if (!posted.RoomNames.Remove(roomName))
        throw new InvalidOperationException("Posted file not found");

      if (posted.RoomNames.Count == 0)
      {
        _postedFiles.Remove(fileId);
        posted.Dispose();
      }
    }
    #endregion
  }
}
