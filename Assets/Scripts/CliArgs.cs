// Command-line switches of the built player (-savefile, -kamion, -jobfresh …). In the editor the same switches can be given
// for play mode through SessionState "Magistrala.args" (space separated, e.g. "-savefile /tmp/x.json -kamion atego"), set from
// an editor script before entering play mode: editor tests never touch the real save in persistentDataPath.
using System;
using System.Collections.Generic;

namespace Magistrala
{
    public static class CliArgs
    {
        public static string[] All
        {
            get
            {
                var a = Environment.GetCommandLineArgs();
#if UNITY_EDITOR
                var s = UnityEditor.SessionState.GetString("Magistrala.args", "");
                if (s.Length > 0)
                {
                    var l = new List<string>(a);
                    l.AddRange(s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    a = l.ToArray();
                }
#endif
                return a;
            }
        }

        public static string Arg(string k)
        {
            var a = All;
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1];
            return null;
        }

        public static bool Has(string k) => Array.IndexOf(All, k) >= 0;
    }
}
