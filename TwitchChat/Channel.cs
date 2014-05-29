﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DarkAutumn.Twitch
{
    public class TwitchChannel
    {
        readonly string r_action = new string((char)1, 1) + "ACTION";
        TwitchConnection m_twitch;

        HashSet<TwitchUser> m_moderators = new HashSet<TwitchUser>();
        Dictionary<string, TwitchUser> m_users = new Dictionary<string, TwitchUser>();

        public delegate void ChannelEventHandler(TwitchChannel channel);
        public delegate void UserEventHandler(TwitchChannel channel, TwitchUser user);
        public delegate void MessageHandler(TwitchChannel channel, TwitchUser user, string text);

        public event MessageHandler MessageReceived;
        public event MessageHandler ActionReceived;

        public event UserEventHandler UserSubscribed;

        public event UserEventHandler ModeratorJoined;
        public event UserEventHandler ModeratorLeft;

        public event UserEventHandler UserChatCleared;
        public event ChannelEventHandler ChatCleared;


        public TwitchUser[] Users
        {
            get
            {
                lock (m_moderators)
                    return m_moderators.ToArray();
            }
        }

        public TwitchConnection Connection { get { return m_twitch; } }

        public bool Connected { get; internal set; }
        public string Name { get; private set; }

        internal TwitchChannel(TwitchConnection twitch, string channel)
        {
            m_twitch = twitch;
            Name = channel;
        }

        public TwitchUser GetUser(string name)
        {
            name = name.ToLower();

            lock (m_users)
            {
                TwitchUser result;
                if (m_users.TryGetValue(name, out result))
                    return result;

                TwitchUserData data = m_twitch.GetUserData(name);
                result = m_users[name] = new TwitchUser(data);
                return result;
            }
        }

        public void Leave()
        {
            m_twitch.Leave(Name);
            m_twitch = null;
            Connected = false;
        }

        internal void NotifyMessageReceived(string username, string line, int offset)
        {
            Debug.Assert(username == username.ToLowerInvariant());
            Debug.Assert(offset < line.Length);

            // Twitchnotify is how subscriber messages "Soandso just subscribed!" comes in:
            TwitchUser user;
            if (username == "twitchnotify")
            {
                int i = line.IndexOf(" just", offset);
                if (i > 0)
                {
                    user = GetUser(line.Slice(offset, i));
                    user.IsSubscriber = true;
                    user.UserData.ImageSet = null; // Need to reparse icon set

                    var evt = UserSubscribed;
                    if (evt != null)
                        evt(this, user);

                    return;
                }
            }

            user = GetUser(username);
            if (line.StartsWith(r_action, offset))
            {
                var evt = ActionReceived;
                if (evt != null)
                    evt(this, user, line.Substring(offset));
            }
            else
            {
                var evt = MessageReceived;
                if (evt != null)
                    evt(this, user, line.Substring(offset));
            }
        }



        internal void ParseModerators(string text, int offset)
        {
            // This room is now in slow mode. You may send messages every 120 seconds
            //*  The moderators of this room are: mod1, mod2, mod3

            string modMsg = "The moderators of this room are: ";
            if (!text.StartsWith(modMsg, offset))
                return;

            offset += modMsg.Length;

            lock (m_moderators)
            {
                TwitchUser streamer = GetUser(Name);

                string[] modList = text.Substring(modMsg.Length).Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                HashSet<TwitchUser> mods = new HashSet<TwitchUser>(modList.Select(name => GetUser(name)));
                mods.Add(streamer);

                foreach (var mod in mods)
                    mod.IsModerator = true;

                var demodded = from mod in m_moderators
                               where !mods.Contains(mod)
                               select mod;

                foreach (var former in demodded)
                    former.IsModerator = false;
            }
        }


        internal void ParseChatClear(string text, int offset)
        {
            string chatclear = "CLEARCHAT";

            if (!text.StartsWith(chatclear, offset))
                return;

            offset += chatclear.Length;

            if (offset+2 > text.Length)
            {
                var evt = ChatCleared;
                if (evt != null)
                    evt(this);
            }
            else
            {
                var evt = UserChatCleared;
                if (evt == null)
                    return;

                offset++;
                var user = GetUser(text.Substring(offset));
                evt(this, user);
            }
        }

        internal void RawJtvMessage(string text, int offset)
        {
            text = text.Substring(offset);
        }

        internal void NotifyModeratorJoined(string user)
        {
            var evt = ModeratorJoined;
            if (evt != null)
                evt(this, GetUser(user));
        }

        internal void NotifyModeratorLeft(string user)
        {
            var evt = ModeratorLeft;
            if (evt != null)
                evt(this, GetUser(user));
        }
    }
}
