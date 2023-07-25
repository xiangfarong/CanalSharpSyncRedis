using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AIStudio.ConSole.Redis.Core
{
    public class PrintHelper
    {
        public static string Bool2String_echo(bool bool_echo)
        {
            return bool_echo ? "OK" : "Fail";
        }

        public static string StringArray2String(string[] strs)
        {
            string str = string.Empty;
            for (int i = 0; i < strs.Length; i++)
            {
                str += $"{i}){strs[i]} ";
            }
            return str;
        }

        public static string ValueTuple2String((string, decimal)[] tuples)
        {
            string str = string.Empty;
            for (int i = 0; i < tuples.Length; i++)
            {
                str += $"{i}){tuples[i].Item1} {tuples[i].Item2} ";
            }
            return str;
        }

        public static string Dictionary2String(Dictionary<string, string> dics)
        {

            string str = string.Empty;
            for (int i = 0; i < dics.Count; i++)
            {
                str += $"{i}){dics.Keys.ToList()[i]} {dics.Values.ToList()[i]} ";
            }
            return str;
        }
    }
}
