﻿using System;
using System.Linq;
using System.Security;
using Engine.Api.Client.Rooms;
using Engine.Api.Server.Messages;
using Engine.Model.Common.Entities;
using Engine.Model.Server;
using Engine.Model.Server.Entities;
using ThirtyNineEighty.BinarySerializer;

namespace Engine.Api.Server.Rooms
{
  [SecurityCritical]
  class ServerExitFromRoomCommand :
    ServerCommand<ServerExitFromRoomCommand.MessageContent>
  {
    public const long CommandId = (long)ServerCommandId.ExitFromRoom;

    public override long Id
    {
      [SecuritySafeCritical]
      get { return CommandId; }
    }

    [SecuritySafeCritical]
    protected override void OnRun(MessageContent content, CommandArgs args)
    {
      if (string.IsNullOrEmpty(content.RoomName))
        throw new ArgumentException("content.RoomName");

      if (content.RoomName == ServerChat.MainRoomName)
      {
        ServerModel.Api.Perform(new ServerSendSystemMessageAction(args.ConnectionId, SystemMessageId.RoomCantLeaveMainRoom));
        return;
      }

      using (var server = ServerModel.Get())
      {
        Room room;
        if (!TryGetRoom(server.Chat, content.RoomName, args.ConnectionId, out room))
          return;

        if (!room.IsUserExist(args.ConnectionId))
        {
          ServerModel.Api.Perform(new ServerSendSystemMessageAction(args.ConnectionId, SystemMessageId.RoomAccessDenied));
          return;
        }

        var closeRoomContent = new ClientRoomClosedCommand.MessageContent { RoomName = room.Name };
        ServerModel.Server.SendMessage(args.ConnectionId, ClientRoomClosedCommand.CommandId, closeRoomContent);

        room.RemoveUser(args.ConnectionId);
        if (room.IsEmpty)
          server.Chat.RemoveRoom(room.Name);
        else
        {
          if (room.Admin == args.ConnectionId)
          {
            room.Admin = room.Users.FirstOrDefault();
            if (room.Admin != UserId.Empty)
              ServerModel.Api.Perform(new ServerSendSystemMessageAction(room.Admin, SystemMessageId.RoomAdminChanged, room.Name));
          }

          RefreshRoom(server.Chat, room);
        }
      }
    }

    [Serializable]
    [BinType("ServerExitFromRoom")]
    public class MessageContent
    {
      [BinField("r")]
      public string RoomName;
    }
  }
}
