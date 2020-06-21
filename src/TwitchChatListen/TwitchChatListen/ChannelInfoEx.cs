using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Drawing;

namespace TwitchChatListen
{
    public class ChannelInfoEx
    {
        //////////////////////////////////////////////////////////////
        // 定数
        //////////////////////////////////////////////////////////////
        private const int MaxMsgCnt = 10000;
        private const int ActiveCalcMin = 5; // アクティブ集計期間(分)

        //////////////////////////////////////////////////////////////
        // 変数
        /////////////////////////////////////////////////////////////
        private string _Name;
        private string _RealName;
        private List<ChannelMessageInfo> ChannelMessages = new List<ChannelMessageInfo>();

        //////////////////////////////////////////////////////////////
        // プロパティ
        /////////////////////////////////////////////////////////////
        /// <summary>
        /// チャンネル名
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value.ToLower();
            }
        }

        /// <summary>
        /// 本当のチャンネル名(立ち見対策)
        /// </summary>
        public string RealName
        {
            get
            {
                return _RealName;
            }
            set
            {
                _RealName = value.ToLower();
            }

        }

        /// <summary>
        /// 配信者名
        /// </summary>
        public string BcName { get; set; }

        /// <summary>
        /// 色
        /// </summary>
        public Color Color { get; set; }

        /// <summary>
        /// 参加中?
        /// </summary>
        public bool IsJoined { get; set; }

        /// <summary>
        /// 存在するチャンネル?
        /// </summary>
        public bool IsExisted { get; set; }

        public bool IsParting { get; set; }

        /// <summary>
        /// ユーザーハッシュテーブル  key:ニックネーム value:ChannelUserInfoEx
        /// </summary>
        public Hashtable UserHashtable { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ChannelInfoEx()
        {
            Name = "";
            BcName = "";
            RealName = "";
            Color = Color.Black;
            IsJoined = false;
            IsExisted = true;
            IsParting = false;
            UserHashtable = new Hashtable();
        }

        /// <summary>
        /// 同一チャンネル/派生チャンネルかどうかを判定する(立ち見対応)
        /// </summary>
        /// <param name="checkName"></param>
        /// <returns></returns>
        public bool IsSameOrDerivedName(string checkName)
        {
            return ChannelInfoEx.IsSameOrDerivedName(this.Name, checkName);
        }

        /// <summary>
        ///  同一チャンネル/派生チャンネルかどうかを判定する
        /// </summary>
        /// <param name="channelName">チャンネル名(JOIN要求したチャンネル名)</param>
        /// <param name="realName">本当のチャンネル名(チェック対象のチャンネル名)</param>
        /// <returns></returns>
        public static bool IsSameOrDerivedName(string channelName, string realName)
        {
            channelName = channelName.ToLower();
            realName = realName.ToLower();
            MatchCollection matches = Regex.Matches(realName, "^" + channelName + "(_1)*$");
            if (matches.Count > 0)
            {
                return true;
            }

            return false;
        }


        /// <summary>
        /// メッセージを追加する
        /// </summary>
        /// <param name="nickName">ニックネーム</param>
        /// <param name="message">メッセージ</param>
        public void AddMessage(string nickName, string message)
        {
            if (MaxMsgCnt == ChannelMessages.Count)
            {
                ChannelMessages.RemoveAt(0);
            }
            ChannelMessages.Add(new ChannelMessageInfo(this.RealName, nickName, message, DateTime.Now));
        }

        /// <summary>
        /// アクティブ数を取得する
        /// </summary>
        /// <returns>アクティブ数</returns>
        public int GetActiveUserCount()
        {
            int activeCnt = 0;
            DateTime fromTime = DateTime.Now.AddMinutes(-ActiveCalcMin); // 5分前～

            // アクティブ集計期間より前のデータ検出用
            Predicate<ChannelMessageInfo> match = delegate (ChannelMessageInfo msg)
            {
                bool matched = false;
                if (fromTime > msg.Time)
                {
                    matched = true;
                }
                return matched;
            };

            // アクティブ集計期間より前のデータは削除する
            ChannelMessages.RemoveAll(match);

            // 算出用に配列へ退避
            ChannelMessageInfo[] msgList = ChannelMessages.ToArray();

            // ニックネームをキーとするハッシュテーブル作成
            Hashtable activeUsers = new Hashtable();
            for (int i = msgList.Length - 1; i >= 0; i--)
            {
                ChannelMessageInfo msg = msgList[i];
                string nickName = msg.NickName;

                if (activeUsers.ContainsKey(nickName))
                {
                    int cnt = (int)activeUsers[nickName];
                    cnt++;
                    activeUsers[msg.NickName] = cnt;
                }
                else
                {
                    activeUsers[msg.NickName] = 1;
                }
            }

            // アクティブ数取得
            activeCnt = activeUsers.Count;

            return activeCnt;
        }
    }
}
