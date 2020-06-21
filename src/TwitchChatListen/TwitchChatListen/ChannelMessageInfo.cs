using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitchChatListen
{
    public class ChannelMessageInfo
    {
        /// <summary>
        /// チャンネル名
        /// </summary>
        public string ChannelName { get; set; }

        /// <summary>
        /// ニックネーム
        /// </summary>
        public string NickName { get; set; }

        /// <summary>
        /// メッセージ
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 時刻
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="nickName">ニックネーム</param>
        /// <param name="message">メッセージ</param>
        /// <param name="time">時刻</param>
        public ChannelMessageInfo(string channelName, string nickName, string message, DateTime time)
        {
            ChannelName = channelName;
            NickName = nickName;
            Message = message;
            Time = time;
        }
    }
}
