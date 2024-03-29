﻿using Google.Protobuf;
using Mirror;
using Mirror.RemoteCalls;
using System;
using System.Collections.Generic;

namespace Channeld
{
    public abstract class ChanneldNetworkBehaviour : NetworkBehaviour
    {
        // Overwrite NetworkBehaviour.SendCommandInternal to make sure the RPC is forwarded to the right channel owner.
        protected override void SendCommandInternal(Type invokeClass, string cmdName, NetworkWriter writer, int channelId, bool requiresAuthority = true)
        {
            // this was in Weaver before
            // NOTE: we could remove this later to allow calling Cmds on Server
            //       to avoid Wrapper functions. a lot of people requested this.
            if (!NetworkClient.active)
            {
                Log.Error($"Command Function {cmdName} called without an active client.");
                return;
            }

            // local players can always send commands, regardless of authority, other objects must have authority.
            if (!(!requiresAuthority || isLocalPlayer || hasAuthority))
            {
                Log.Warning($"Trying to send command for object without authority. {invokeClass}.{cmdName}");
                return;
            }

            // previously we used NetworkClient.readyConnection.
            // now we check .ready separately and use .connection instead.
            if (!NetworkClient.ready)
            {
                Log.Error("Send command attempted while NetworkClient is not ready.");
                return;
            }

            // IMPORTANT: can't use .connectionToServer here because calling
            // a command on other objects is allowed if requireAuthority is
            // false. other objects don't have a .connectionToServer.
            // => so we always need to use NetworkClient.connection instead.
            // => see also: https://github.com/vis2k/Mirror/issues/2629
            if (NetworkClient.connection == null)
            {
                Log.Error("Send command attempted with no client running.");
                return;
            }

            // construct the message
            CommandMessage message = new CommandMessage
            {
                netId = netId,
                componentIndex = (byte)ComponentIndex,
                // type+func so Inventory.RpcUse != Equipment.RpcUse
                functionHash = RemoteCallHelperExposed.GetMethodHash(invokeClass, cmdName),
                // segment to avoid reader allocations
                payload = writer.ToArraySegment()
            };

            ChanneldTransport.Current.ClientSendNetworkMessage(ChannelDataView.GetOwningChannel(netId), message);
        }

        protected virtual void OnChannelDataRemoved()
        {
            if (isClient)
            {
                NetworkClient.DestroyObject(netId);
            }
            else
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        public override void OnStartServer()
        {
            var cnm = NetworkManager.singleton as ChanneldNetworkManager;
            AddDataProviders(cnm?.CurrentView);
        }
        public override void OnStopServer()
        {
            var cnm = NetworkManager.singleton as ChanneldNetworkManager;
            RemoveDataProviders(cnm?.CurrentView);
        }
        public override void OnStartClient()
        {
            var cnm = NetworkManager.singleton as ChanneldNetworkManager;
            AddDataProviders(cnm?.CurrentView);
        }
        public override void OnStopClient()
        {
            var cnm = NetworkManager.singleton as ChanneldNetworkManager;
            RemoveDataProviders(cnm?.CurrentView);
        }

        private void AddDataProviders(ChannelDataView view)
        {
            if (view == null)
            {
                Log.Error("ChannelDataView is not properly initialized.");
                return;
            }

            /* A ChanneldNetworkBehaviour can implement multiple IChannelDataProvider<T>
            foreach (var interfaceType in GetType().GetInterfaces())
            {
                if (interfaceType.IsSubclassOf(typeof(IChannelDataProvider<>)))
                {
                    view.AddChannelDataProviderToDefaultChannel((IChannelDataProvider)this, interfaceType.GetGenericArguments()[0]);
                }
            }
            */

            if (this is IChannelDataProvider)
            {
                view.AddChannelDataProviderToDefaultChannel((IChannelDataProvider)this);
            }
        }

        private void RemoveDataProviders(ChannelDataView view)
        {
            if (view == null)
            {
                Log.Error("ChannelDataView is not properly initialized.");
                return;
            }

            /* A ChanneldNetworkBehaviour can implement multiple IChannelDataProvider<T>
            foreach (var interfaceType in GetType().GetInterfaces())
            {
                if (interfaceType.IsSubclassOf(typeof(IChannelDataProvider<>)))
                {
                    view.RemoveChannelDataProviderFromAllChannels((IChannelDataProvider)this, interfaceType.GetGenericArguments()[0]);
                }
            }
            */

            if (this is IChannelDataProvider)
            {
                view.RemoveChannelDataProviderFromAllChannels((IChannelDataProvider)this, true);
            }
        }
    }
}
