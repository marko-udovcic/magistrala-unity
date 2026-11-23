// Marko's package files the editor builders read, in ONE place. His folders come and go (~/Documents/mag-23-11-v2 was gone on
// 24. 11.), so the files live in the BalkanDriver repo (tools/world/krizanja/paket-2411/, written by tools/world/krizanja/paket-2411.py):
//   krizanja.json, dionice.json   mag-24-11 merged view (Kaštela part; 06 / 07 / 08 / 60 as in the game) – SignsBuilder (zebras, `dir`)
//   ceste_izvan_modela.json       mag-24-11 outside roads without Solin (access-f*) and the bay bridge (access-g*) – OutsideStreets
//   drvoredi.json                 mag-24-11 surveyed street trees and parks (replaces 23-11-v2) – Drvoredi
//   tocke.json                    checkpoints + tours of world_1to1.json (= mag-11-19), Plodine / Stop Shop Sućurac at their yards – Tours
// A missing file is an error that stops the build step (it used to give 0 outside streets / 0 delivery points without a word).
using System.IO;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class PaketData
    {
        public const string DIR = W.BD + "/tools/world/krizanja/paket-2411/";
        public const string KRIZANJA = DIR + "krizanja.json";
        public const string DIONICE = DIR + "dionice.json";
        public const string CESTE = DIR + "ceste_izvan_modela.json";
        public const string TOCKE = DIR + "tocke.json";
        public const string DRVOREDI = DIR + "drvoredi.json";

        /// the path if the file is there, else a loud error and an exception (the build step stops)
        public static string Need(string path)
        {
            if (File.Exists(path)) return path;
            Debug.LogError($"[paket] NEDOSTAJE {path} – paket nije na disku (BalkanDriver tools/world/krizanja/paket-2411.py)");
            throw new FileNotFoundException("paket: " + path, path);
        }
    }
}
