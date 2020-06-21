using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing; // Color

namespace TwitchChatListen
{
    public class ChannelUserInfoEx
    {
        //////////////////////////////////////////////////////////////
        // 変数
        /////////////////////////////////////////////////////////////
        private string channelName;

        //////////////////////////////////////////////////////////////
        // プロパティ
        /////////////////////////////////////////////////////////////
        /// <summary>
        /// ニックネーム
        /// </summary>
        public string NickName { get; set; }

        /// <summary>
        /// ホスト
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// チャンネル名(立ち見も区別する)
        /// </summary>
        public string ChannelName
        {
            get
            {
                return channelName;
            }
            set
            {
                channelName = value.ToLower();// 小文字で格納
            }
        }

        /// <summary>
        /// 色
        /// </summary>
        public Color Color { get; set; }

        /// <summary>
        /// 配信者?
        /// </summary>
        public bool IsBc { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ChannelUserInfoEx()
        {
            NickName = "";
            Host = "";
            ChannelName = "";
            Color = Color.Black;
            IsBc = false;
        }
    }
}
