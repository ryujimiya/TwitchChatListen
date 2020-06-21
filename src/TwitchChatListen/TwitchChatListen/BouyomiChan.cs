using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using FNF.Utility; // BouyomiChanClient

namespace MyUtilLib
{
    // NOTE: 棒読みちゃん本体で下記設定を行ってください。
    //  (1) 配信者向け機能を有効にする
    //  (2) システム - 基本 02)読み上げ関連 - 02)行間の待機時間 を0に設定する
    class BouyomiChan : IDisposable
    {
        //////////////////////////////////////////////////////////////
        // 型
        //////////////////////////////////////////////////////////////

        //////////////////////////////////////////////////////////////
        // 定数
        //////////////////////////////////////////////////////////////

        //////////////////////////////////////////////////////////////
        // 変数
        //////////////////////////////////////////////////////////////
        /// <summary>
        /// 破棄された？
        /// </summary>
        private bool disposed = false;
        /// <summary>
        /// 棒読みちゃんクライアント
        /// </summary>
        private BouyomiChanClient bouyomiChanClient = null;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public BouyomiChan()
        {
            init();
        }
        
        /// <summary>
        /// ファイナライザ
        /// </summary>
        ~BouyomiChan()
        {
            Dispose(false);
        }

        /// <summary>
        /// 初期化
        /// </summary>
        private void init()
        {
            if (disposed)
            {
                return;
            }

            // 棒読みちゃんクライアント
            bouyomiChanClient = new BouyomiChanClient();
        }

        /// <summary>
        /// 終了
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// 使用中のリソースをすべてクリーンアップします。
        /// </summary>
        /// <param name="disposing">マネージ リソースが破棄される場合 true、破棄されない場合は false です。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                // 棒読みちゃんクライアント破棄処理
                if (bouyomiChanClient != null)
                {
                    bouyomiChanClient.Dispose();
                    bouyomiChanClient = null;
                }

                disposed = true;
            }
        }

        /// <summary>
        /// 未実行の読み上げテキストをクリアする
        /// </summary>
        public void ClearText()
        {
            try
            {
                bouyomiChanClient.ClearTalkTasks();
            }
            //catch (System.Runtime.Remoting.RemotingException exception)
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
            }
        }

        /// <summary>
        /// 指定されたテキストの音声を出力する
        /// （スレッドにタスクをキューイングする)
        /// </summary>
        /// <param name="text">テキスト</param>
        /// <returns></returns>
        public bool Talk(string text)
        {
            System.Diagnostics.Debug.WriteLine("BouyomiChan::Talk:" + text);

            int speed = -1;
            int tone = -1;
            int volume = -1;
            VoiceType voiceType = VoiceType.Default;
            try
            {
                // 棒読みちゃん本体へタスク追加
                bouyomiChanClient.AddTalkTask(text, speed, tone, volume, voiceType);
            }
            //catch (System.Runtime.Remoting.RemotingException exception)
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
            }

            return true;
        }
    }
}
