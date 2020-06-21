using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices; //DllImport
using System.Collections; //Hashtable
using System.Text.RegularExpressions; // Regex
using System.Drawing; // Color

namespace MyUtilLib
{
    class MyUtil
    {
        //////////////////////////////////////////////////////////////
        // DLL
        //////////////////////////////////////////////////////////////
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);
        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = false)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("winmm.dll")]
        public static extern int waveOutGetVolume(IntPtr hwo, out uint dwVolume);

        [DllImport("winmm.dll")]
        public static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

        /// <summary>
        /// DLLの関数をデリゲートとして取得
        /// </summary>
        /// <param name="libHandle"></param>
        /// <param name="functionName"></param>
        /// <param name="type">デリゲートの型(typeof()で指定)</param>
        /// <returns>Delegate</returns>
        public static Delegate GetDelegateForLibFunction(IntPtr libHandle, string functionName, Type type)
        {
            IntPtr funcPtr;
            funcPtr = MyUtil.GetProcAddress(libHandle, functionName);
            if (funcPtr == IntPtr.Zero)
            {
                throw (new Exception(functionName + " does not exist."));
            }
            return Marshal.GetDelegateForFunctionPointer(funcPtr, type);
        }

        /// <summary>
        /// 音量設定
        /// </summary>
        /// <param name="volume"></param>
        public static void WaveOutSetVolume(int volume)
        {
            int num = 0x28f * volume; //0が音量　100が最大？
            uint dwVolume2 = (uint)((num & 0xffff) | (num << 0x10));
            waveOutSetVolume(IntPtr.Zero, dwVolume2);
        }

        /// <summary>
        /// 音量取得
        /// </summary>
        /// <returns></returns>
        public static int WaveOutGetVolume()
        {
            int volume;
            uint dwVolume = 0;
            waveOutGetVolume(IntPtr.Zero, out dwVolume);
            ushort num2 = (ushort)(dwVolume & 0xffff);
            volume = num2 / 0x28f;
            return volume;
        }

        /// <summary>
        /// カタカナ→ひらがな変換
        /// 参照設定に「Microsoft.VisualBasic」追加
        /// </summary>
        /// <param name="katakana"></param>
        /// <returns></returns>
        public static string KakakanaToHiragana(string katakana)
        {
            return Microsoft.VisualBasic.Strings.StrConv(katakana, Microsoft.VisualBasic.VbStrConv.Hiragana, 0);
        }

        /// <summary>
        /// ひらがな→カタカナ変換
        /// 参照設定に「Microsoft.VisualBasic」追加
        /// </summary>
        /// <param name="hiragana"></param>
        /// <returns></returns>
        public static string HiraganaToKakakana(string hiragana)
        {
            return Microsoft.VisualBasic.Strings.StrConv(hiragana, Microsoft.VisualBasic.VbStrConv.Katakana, 0);
        }

        /*参照設定に「Microsoft.VisualBasic」追加*/
        /// <summary>
        /// Trim
        /// 参照設定に「Microsoft.VisualBasic」追加
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string Trim(string str)
        {
            return Microsoft.VisualBasic.Strings.Trim(str);
        }

        /// <summary>
        /// SafetyOperate
        /// </summary>
        /*
        public static object SafetyOperate(Control context, Delegate method)
        {
            return MyUtil.SafelyOperate(context, method, null);
        }

        public static object SafelyOperate(Control context, Delegate method, params object[] args)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            if (method == null)
            {
                throw new ArgumentNullException("method");
            }

            if (!(context.IsHandleCreated))
            {
                return null;
            }
            if (context.InvokeRequired)
            {
                return context.Invoke(method, args);
            }
            else
            {
                return method.DynamicInvoke(args);
            }
        }
        */

        /// <summary>
        /// コマンドライン引数解析
        /// /key0 /Key1 Value1 /Key2 Value2 Value3
        ///   key0の値はnull
        ///   key1の値はValue1
        /// </summary>
        /// <param name="keyPrefix">(初期値: '/')</param>
        /// <returns></returns>
        public class CmdLineInfo
        {
            public string[] Args = null;                   // コマンドライン引数配列 System.Environment.GetCommandLineArgs()を格納
            public string AppPath = "";                    // コマンドライン引数の先頭パラメータ(アプリケーションパス)
            public Hashtable ParamHash = new Hashtable();  // key: パラメータキー value:パラメータ値
            public string[] ParamValues = null;             // k
        }
        public static CmdLineInfo GetCmdLineInfo(char keyPrefix = '/')
        {
            CmdLineInfo cmdLineInfo = new CmdLineInfo();

            //コマンドラインを配列で取得する
            string[] args = System.Environment.GetCommandLineArgs();
            cmdLineInfo.Args = args;

            // アプリケーションパス
            cmdLineInfo.AppPath = args[0];
            // 引数解析
            if (args.Length > 1)
            {
                Hashtable paramHash = cmdLineInfo.ParamHash;
                List<string> paramValueList = new List<string>();
                string paramKey = "";
                string paramValue = "";
                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg == null || arg.Length == 0)
                    {
                        continue;
                    }
                    if (arg[0] == keyPrefix)
                    {
                        // キー
                        paramKey = arg;
                        paramHash[paramKey] = null;
                    }
                    else
                    {
                        // 値
                        paramValue = arg;
                        if (paramKey != "")
                        {
                            // キー付きパラメータ値
                            paramHash[paramKey] = paramValue;
                            paramKey = "";
                        }
                        else
                        {
                            // キーなしパラメータ値
                            paramValueList.Add(paramValue);
                        }
                    }
                }
                if (paramValueList.Count > 0)
                {
                    cmdLineInfo.ParamValues = paramValueList.ToArray();
                }
            }
            return cmdLineInfo;
        }

        /// <summary>
        /// URLを抽出する
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string GetUrlInText(string text)
        {
            string url = "";
            ////////////text = "うらるんhttp://yahoo.co.jp/です";
            MatchCollection matches = Regex.Matches(text, "(https?(://[-_.!~*\'()a-zA-Z0-9;/?:@&=+$,%#]+))");
            if (matches.Count > 0)
            {
                url = matches[0].Groups[1].Value;  // $1の箇所
            }
            return url;
        }

        /// <summary>
        /// 複数のURLを抽出する
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string[] GetUrlListInText(string text)
        {
            string[] urls = null;
            ////////////text = "うらるんhttp://yahoo.co.jp/です";
            MatchCollection matches = Regex.Matches(text, "(https?(://[-_.!~*\'()a-zA-Z0-9;/?:@&=+$,%#]+))");
            if (matches.Count > 0)
            {
                int i = 0;
                urls = new string[matches.Count];
                foreach (Match match in matches)
                {
                    string url;
                    url = matches[0].Groups[1].Value;  // $1の箇所
                    urls[i] = url;
                    i++;
                }
            }
            return urls;
        }

        /// <summary>
        /// 指定桁数の乱数値を取得する
        ///   int num = GetRandIntWithKeta(5, 6); //5桁以上6桁未満
        /// </summary>
        /// <param name="minKeta">最小桁数</param>
        /// <param name="maxKeta">最大桁数</param>
        /// <returns>指定桁数の乱数値</returns>
        public static int GetRandIntWithKeta(int minKeta, int maxKeta)
        {
            Random random = new Random();
            int num = random.Next((int)Math.Pow(10, minKeta), (int)Math.Pow(10, maxKeta));
            return num;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxValue"></param>
        /// <returns></returns>
        public static int GetRandInt(int maxValue)
        {
            Random random = new Random();
            int num = random.Next(maxValue);
            return num;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        /// <returns></returns>
        public static int GetRandInt(int minValue, int maxValue)
        {
            Random random = new Random();
            int num = random.Next(minValue, maxValue);
            return num;
        }

        /// <summary>
        /// 乱数を取得する
        /// </summary>
        /// <param name="max">0以上最大値未満の乱数値</param>
        /// <returns></returns>
        public static int GetExactRandInt(int maxValue)
        {
            return GetRandInt(0, maxValue);
        }

        /// <summary>
        ///  乱数を取得する
        /// </summary>
        /// <param name="min">最小値</param>
        /// <param name="max">最大値</param>
        /// <returns>最小値以上最大値未満の乱数値</returns>
        public static int GetExactRandInt(int minValue, int maxValue)
        {
            //Int32と同じサイズのバイト配列にランダムな値を設定する
            //byte[] bs = new byte[sizeof(int)];
            byte[] bs = new byte[4];
            System.Security.Cryptography.RNGCryptoServiceProvider rng =
                new System.Security.Cryptography.RNGCryptoServiceProvider();
            rng.GetBytes(bs);

            //Int32に変換する
            int value = System.BitConverter.ToInt32(bs, 0); // 符号なし整数
            value = Math.Abs(value) % (maxValue - minValue) + minValue;
            return value;
        }

        /// <summary>
        /// バージョン名を取得
        /// </summary>
        /// <returns></returns>
        public static string GetFileVersion()
        {
            string versionStr = "";
            System.Diagnostics.FileVersionInfo versionInfo =
                System.Diagnostics.FileVersionInfo.GetVersionInfo(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            versionStr = versionInfo.FileVersion;
            return versionStr;
        }

        /// <summary>
        /// UNIX時間の取得
        /// </summary>
        /// <param name="targetTime"></param>
        /// <returns></returns>
        public static long GetUnixTime(DateTime targetTime)
        {
            // UTC時間に変換
            targetTime = targetTime.ToUniversalTime();

            // UNIXエポックからの経過時間を取得
            DateTime UNIX_EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan elapsedTime = targetTime - UNIX_EPOCH;

            // 経過秒数に変換
            return (long)elapsedTime.TotalSeconds;
        }
    }
}
