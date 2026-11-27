// Package inputs shared by every editor builder. paket-2711 contains the
// published 2511 models plus mag-27-11 models 66–82 and their shortened outside
// streets. Checkpoints, sections and surveyed trees are carried forward by the
// BalkanDriver package importer. A missing input stops the build.
using System.IO;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class PaketData
    {
        public const string DIR = W.BD + "/tools/world/krizanja/paket-2711/";
        public const string KRIZANJA = DIR + "krizanja.json";
        public const string DIONICE = DIR + "dionice.json";
        public const string CESTE = DIR + "ceste_izvan_modela.json";
        public const string TOCKE = DIR + "tocke.json";
        public const string DRVOREDI = DIR + "drvoredi.json";
        public const string RIVA_MASKA = DIR + "riva-maska.json";
        public const string STANICE = DIR + "autobusna_stajalista.json";

        /// the path if the file is there, else a loud error and an exception (the build step stops)
        public static string Need(string path)
        {
            if (File.Exists(path)) return path;
            Debug.LogError($"[paket] NEDOSTAJE {path} – paket nije na disku (BalkanDriver tools/world/krizanja/paket-2711.py)");
            throw new FileNotFoundException("paket: " + path, path);
        }
    }
}
