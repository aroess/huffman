using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Xml.Serialization;
using CommandLine;

namespace huffman
{
    class Options
    {
        [Option('d', "decode", Required = false,
          HelpText = "Datei dekodieren.")]
        public bool decode { get; set; }

        [ValueList(typeof(List<string>), MaximumElements = 2)]
        public IList<string> Items { get; set; }

        [Option('v', "verbose", DefaultValue = false,
          HelpText = "Ausfühlerlichen Bericht anzeigen.")]
        public bool Verbose { get; set; }

        [Option('h', "help", DefaultValue = false,
          HelpText = "Hilfe anzeigen.")]
        public bool Help { get; set; }

        [ParserState]
        public IParserState LastParserState { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            var usage = new StringBuilder();
            usage.AppendLine("Huffman Enkodierer/Dekodierer");
            usage.AppendLine("Standardeinstellung: Enkodieren");
            usage.AppendLine("Syntax: huffman [Optionen] Quelldatei Zieldatei");
            usage.AppendLine("Optionen:");
            usage.AppendLine(" -v Ausführliche Informationen");
            usage.AppendLine(" -d Datei dekodieren");
            usage.AppendLine(" -h Diese Hilfe anzeigen");
            return usage.ToString();
        }
    }
    class HuffTree
    {
        private BNode root = null;
        private List<BNode> nodeList = new List<BNode>();
        private List<BNode> noParents = new List<BNode>();
        private Dictionary<char, string> huffmanTable
            = new Dictionary<char, string>();
        private StringBuilder completeString = new StringBuilder(1000000);

        private void addNode(BNode node)
        {
            this.nodeList.Add(node);
        }
        private void mergeNodes(BNode min1, BNode min2)
        {
            BNode node = new BNode();
            node.chr = Char.MinValue;
            node.count = min1.count + min2.count;
            min1.parent = node; min2.parent = node;
            min1.edge = 0; min2.edge = 1; // löschen!
            node.leftChild = min1; node.rightChild = min2;
            nodeList.Add(node);
        }
        public void generate(bool decode, string source)
        {
            Dictionary<char, Int64> charList;
            // Zum Dekodieren wird die Zeichenfrequenz aus der XML-Datei ausgelesen
            if (decode)
            {
                charList = this.readXML();
            }
            // Zum Enkodieren wird die Zeichenfrequenz berechnet und gespeichert
            else
            {
                StreamReader sr = new StreamReader(source);

                // Berechne Zeichenfrequenz
                charList = new Dictionary<char, Int64>();
                this.completeString.Append(sr.ReadToEnd());
                foreach (char c in this.completeString.ToString())
                {
                    if (!charList.ContainsKey(c)) charList.Add(c, 0);
                    charList[c] += 1;
                }
                sr.Close();

                // Speichern
                this.writeXML(charList);
            }
            // Grundgerüst des Binärbaums generieren
            foreach (KeyValuePair<char, Int64> pair in charList)
            {
                BNode node = new BNode();
                node.chr = pair.Key;
                node.count = pair.Value;
                node.parent = null; // alle Knoten ohne Eltern initialisieren
                this.addNode(node);
            }
            do
            {   /* Solange es mehr als einen Knoten gibt, zu dem keine Kante hinführt, wiederhole:
                 * - suche zwei Knoten u und v mit minimaler Markierung p(u) bzw. p(v), zu denen noch keine Kante hinführt
                 * - erzeuge einen neuen Knoten w und verbinde w mit u und v; markiere die eine Kante mit 0,
                 * - die andere mit 1; markiere den Knoten w mit p(u) + p(v) */
                this.noParents = nodeList
                    .Where(node => !node.hasParent())
                    .OrderBy(node => node.count)
                    .ToList<BNode>();
                if(this.noParents.Count <= 1)
                {
                    this.root = noParents[0]; // Letzter Knoten muss Wurzelknoten sein
                    break;  // Beende While-Schleife
                }
                this.mergeNodes(noParents[0], noParents[1]);
            } while (true);
        }
        public void huffmanEncode(bool verbose, BNode node = null, string str = "")
        {
            try
            {
                if (node == null) node = this.root;

                if (node.chr == Char.MinValue)
                {
                    huffmanEncode(verbose, node.leftChild, str + "0");
                    huffmanEncode(verbose, node.rightChild, str + "1");
                }
                else
                {
                    this.huffmanTable.Add(node.chr, str);
                    if (verbose) Console.WriteLine("{0}={1}", node.chr, str);
                }
            }
            catch (System.ArgumentException)
            {
                Console.WriteLine("Fehler: Uneindeutiges Zeichen gefunden. Stellen sie sicher, dass es sich bei der Quelldatei um eine Textdatei handelt.");
                System.Environment.Exit(0);
            }

        }
        public void huffmanDecode(BNode node, BitArray bits, string destination)
        {
            StreamWriter sw = new StreamWriter(destination);
            int i = 0; // bit index
            int completeBytes = ((int)bits.Length / 8);

            while (i < completeBytes * 8)
            {
                if (node == null) node = this.root;

                // Blatt gefunden, beginne von vorn
                if (node.chr != Char.MinValue)
                {
                    sw.Write(node.chr);
                    node = null;
                    continue;
                }

                else // kein Blatt gefunden, durchlaufe Baum weiter
                {
                    if (bits[i] == false)
                    {
                        node = node.leftChild;
                        i++;
                        continue;
                    }
                    else
                    {
                        node = node.rightChild;
                        i++;
                        continue;
                    }
                }
            }
            sw.Close();
        }
        public void writeBinaryFile(string destination)
        {
            // Erzeuge String
            StringBuilder result = new StringBuilder(1000000);
            foreach (char c in this.completeString.ToString())
            {
                result.Append(this.huffmanTable[c]);
            }
            result.ToString();

            // Schreibe Binärdatei
            FileStream bw = new FileStream(destination, FileMode.Create, FileAccess.Write);

            int limit = ((int)result.Length / 8) * 8 + 8; // z.B. int(262/8) = 32, 32*8 = 256; +8 für inkomplettes Byte
            int overflow = result.Length % 8;

            bool[] extractBits = new bool[limit];
            // Konvertiere von String -> Bool
            for (int i = 0; i < limit; i++)
            {
                if (i < result.Length)
                    extractBits[i] = (result[i] == '1' ? true : false);
                else // fülle inkomplettes Byte mit false auf
                    extractBits[i] = false;
            }
            // Lese bits in ein BitArray ein
            BitArray bit8 = new BitArray(extractBits);
            // Konvertiere BitArray in ByteArray
            byte[] bytes = new byte[limit / 8]; // z.B. 256 bits => 256/8=32 bytes
            bit8.CopyTo(bytes, 0);              // SCHREIBT BITS IN UMGEKEHRTER REIHENFOLGE
            bw.Write(bytes, 0, bytes.Length);   // schreibe alle bytes in die Datei
            bw.Close();
        }
        public BitArray readBinaryFile(string source)
        {
            FileInfo f = new FileInfo(source);
            long limit = f.Length; // in bytes

            // Lese Binärdatei
            FileStream br = new FileStream(source, FileMode.Open, FileAccess.Read);
            byte[] readBytes = new byte[limit];
            br.Read(readBytes, 0, readBytes.Length);
            br.Close();

            return new BitArray(readBytes);
        }
        private void writeXML(Dictionary<char, Int64> charList)
        {
            // https://stackoverflow.com/questions/12554186
            StreamWriter sw = new StreamWriter("char_freq");
            XmlSerializer serializer = new XmlSerializer(typeof(HuffItemXML[]),
                 new XmlRootAttribute() { ElementName = "items" });
            serializer.Serialize(sw,
                charList.Select(kv => new HuffItemXML() { id = kv.Key, value = kv.Value.ToString() }).ToArray());
            sw.Close();
        }
        private Dictionary<char, Int64> readXML()
        {
            StreamReader sr = new StreamReader("char_freq");
            XmlSerializer serializer = new XmlSerializer(typeof(HuffItemXML[]),
                 new XmlRootAttribute() { ElementName = "items" });
            Dictionary<char, Int64> charList = ((HuffItemXML [])serializer.Deserialize(sr))
               .ToDictionary(i => (char)i.id, i => Int64.Parse(i.value));
            sr.Close();
            return charList;
        }
    }
    class BNode
    {
        public char chr { get; set; }
        public long count { get; set; }
        public int edge { get; set; }
        public BNode parent { get; set; }
        public BNode leftChild { get; set; }
        public BNode rightChild { get; set; }

        public bool hasParent()
        {
            if (this.parent != null) return true;
            else return false;
        }
    }
    public class HuffItemXML
    {
        [XmlAttribute]
        public int id;
        [XmlAttribute]
        public string value;
    }
    class Program
    {
        static void Main(string[] args)
        {
            var options = new Options();
            // Beende bei Angabe fehlerhafter Optionen
            if (!CommandLine.Parser.Default.ParseArguments(args, options))
            {
                System.Environment.Exit(0);
            }
            if (options.Help)
            {
                Console.Write(options.GetUsage());
                System.Environment.Exit(0);
            }
            if (options.Items.Count != 2)
            {
                Console.Write("Bitte Quell- und Zieldatei angeben.");
                System.Environment.Exit(0);
            }
            if (!File.Exists(options.Items.ElementAt(0)))
            {
                Console.Write("Datei nicht gefunden: " + options.Items.ElementAt(0));
                System.Environment.Exit(0);
            }

            Stopwatch timePerParse = Stopwatch.StartNew();

            HuffTree t = new HuffTree();
            string source = options.Items.ElementAt(0);
            string destination = options.Items.ElementAt(1);

            // Standardeinstellung: Enkodieren
            if (!options.decode)
            {
                // erzeugt Huffman-Baum
                t.generate(options.decode, source);

                // erzeuge Huffman-Tabelle (Zeichen -> Binärzeichenkette), Huffman-Baum wird durchlaufen
                t.huffmanEncode(options.Verbose);

                // übergebe Originalzeichenkette, wird anhand der Huffmann-Tabelle codiert und gespeichert
                t.writeBinaryFile(destination);
            }
            else
            {
                // erzeugt Huffman-Baum, charList wird aus XML-Datei ausgelesen
                t.generate(options.decode, null);

                // Binärdatei auslesen
                BitArray bArray = t.readBinaryFile(source);

                // Dekodieren und speichern
                t.huffmanDecode(null, bArray, destination);
            }

            timePerParse.Stop();

            if (!options.decode && options.Verbose)
            {
                long fls = (new FileInfo(source)).Length;
                long fld = (new FileInfo(destination)).Length;
                Console.WriteLine("Dateigröße der Quelldatei: " + fls);
                Console.WriteLine("Dateigröße der Zieldatei: " + fld);
                Console.WriteLine("Kompressionsrate: " + Math.Round(
                    (Decimal)((float)fld / (float)fls * 100), 2, MidpointRounding.AwayFromZero) + "%");
                Console.WriteLine("Benötigte Zeit: " + timePerParse.ElapsedMilliseconds + "ms");
            }
        }
    }
}
