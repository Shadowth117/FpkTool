using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Reloaded.Memory.Streams;
using ZstdNet;
using System.Diagnostics;
using System.Text;
using UnluacNET;

namespace FpkToolPreNGS
{
    class Program
    {
        struct FpkHeader
        {
            public uint fpk;
            public uint zero1;
            public uint zero2;
            public uint count;
        }

        struct FileEntry
        {
            public ulong hash;
            public uint compressedSize;
            public uint offset;
            public uint uncompressedSize;
        }

        static void Main(string[] args)
        {
            if (args.Count() == 0)
            {
                PrintUsage();
                return;
            }

            if(args[0] == "-hash")
            {
                for (int i = 1; i < args.Length; i++)
                {
                    Console.WriteLine(CalcNameHash(args[i]).ToString() + $" {args[i]}");
                }
                return;
            }
            if (args[0] == "-ripLuaNames")
            {
                RipLuaText(args[1]);
                return;
            }
            if (args.Count() == 2)
            {
                CompareLua(args[0], args[1]);
            }
            else
            {
                var path = Path.GetFullPath(args[0]);
                if (File.Exists(path))
                {
                    DumpFpk(path);
                }
                else if (Directory.Exists(path))
                {
                    //PackFpk(path); 
                }
                else
                {
                    PrintUsage();
                }
            }
        }

        public static void RipLuaText(string path)
        {
            var fileDict = new Dictionary<ulong, string>();
            string txtFile = "FpkToolNGS.txt";
            var refText = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase).Substring(6), txtFile);

            //Get name references if they exist
            if (File.Exists(refText))
            {
                Console.WriteLine("FpkToolNGS.txt exists");
                var text = File.ReadAllLines(refText);
                foreach (var line in text)
                {
                    string[] fileKey = line.Split(' ');
                    var key = ulong.Parse(fileKey[0], System.Globalization.NumberStyles.HexNumber);
                    if (fileDict.ContainsKey(key))
                    {
                        continue;
                    }
                    fileDict.Add(key, fileKey[1]);
                }
            }
            else
            {
                Console.WriteLine("FpkToolNGS.txt does not exist");
            }

            var filePaths = Directory.EnumerateFiles(path, "*.decomplua", SearchOption.AllDirectories);
            foreach(var filePath in filePaths)
            {
                var txt = File.ReadAllLines(filePath);
                foreach(var line in txt)
                {
                    if(line.Contains(".lua"))
                    {
                        var textSet = line.Split('"');
                        string luaStr = "";
                        foreach( var txtChunk in textSet)
                        {
                            if(txtChunk.Contains(".lua"))
                            {
                                luaStr = txtChunk;
                            }
                        }
                        textSet = luaStr.Split('/');
                        foreach (var txtChunk in textSet)
                        {
                            if (txtChunk.Contains(".lua"))
                            {
                                luaStr = txtChunk;
                            }
                        }
                        textSet = luaStr.Split('\\');
                        foreach (var txtChunk in textSet)
                        {
                            if (txtChunk.Contains(".lua"))
                            {
                                luaStr = txtChunk;
                            }
                        }

                        var hash = ulong.Parse(CalcNameHash(luaStr), System.Globalization.NumberStyles.HexNumber);

                        if(!fileDict.ContainsKey(hash))
                        {
                            fileDict.Add(hash, luaStr);
                        }
                    }
                }
            }

            var fileDictReverse = new Dictionary<string, ulong>();
            foreach (var line in fileDict)
            {
                fileDictReverse.Add(line.Value, line.Key);
            }
            var keyListAlpha = fileDictReverse.Keys.ToList();
            keyListAlpha.Sort();

            var fileList = new List<string>();
            foreach(var key in keyListAlpha)
            {
                fileList.Add($"{fileDictReverse[key]:X16}" + " " + key);
            }
            File.WriteAllLines(refText.Replace(".txt", "_new.txt"), fileList);
        }

        public static string CalcNameHash(string name)
        {
            var nameArr = Encoding.ASCII.GetBytes(name);
            byte AC = 0;
            var xHash = 0xCBF29CE484222325;
            for(int i = 0; i < name.Length; i++)
            {
                AC = nameArr[i];
                xHash = (xHash ^ AC) * 0x100000001B3;
            }
            var arr = BitConverter.GetBytes(xHash);
            string outStr = "";
            for(int i = arr.Length - 1; i > -1; i--)
            {
                outStr += $"{arr[i]:X2}";
            }

            return outStr;
        }
        
        static void PrintUsage()
        {
            Console.WriteLine("FpkTool");
            Console.WriteLine("usage: FpkTool <path to fpk>");
            Console.WriteLine("       FpkTool <path to extracted fpk folder>");
            Console.WriteLine("       FpkTool <path to unnamed fpk folder> <path to named fpk folder>");
            Console.WriteLine("       FpkTool -hash <name of lua to hash (only filename, no path)>");
            Console.WriteLine("ex. FpkTool.exe " + @"H:\pso2_bin\data\win32\00800bcb4e790060f5a47b85fbc2acd0, ");
        }

        static void DumpFpk(string filePath)
        {
            var fileDict = new Dictionary<ulong, string>();
            var storeDict = new Dictionary<ulong, byte[]>();
            var luaFiles = new List<string>();
            string txtFile = "FpkToolNGS.txt";
            var refText = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase).Substring(6), txtFile);

            //Get name references if they exist
            if (File.Exists(refText))
            {
                Console.WriteLine("FpkToolNGS.txt exists");
                var text = File.ReadAllLines(refText);
                foreach(var line in text)
                {
                    string[] fileKey = line.Split(' ');
                    var key = ulong.Parse(fileKey[0], System.Globalization.NumberStyles.HexNumber);
                    if(fileDict.ContainsKey(key))
                    {
                        continue;
                    }
                    fileDict.Add(key, fileKey[1]);
                }
            }
            else
            {
                Console.WriteLine("FpkToolNGS.txt does not exist");
            }

            //Extract files and apply names if applicable
            Console.WriteLine("Proceeding...");
            List<FileEntry> table = new List<FileEntry>();
            if (File.Exists(filePath))
            {
                byte[] zstdDict = new byte[0];
                ulong zstdDictHash = 0;
                var fpk = File.ReadAllBytes(filePath);
                using (var fileStream = new FileStream(filePath, FileMode.Open))
                using (var streamReader = new BufferedStreamReader(fileStream, 8192))
                {

                    streamReader.Seek(0xC, SeekOrigin.Begin);
                    streamReader.Read<int>(out int entryCount);
                    Console.WriteLine(streamReader.Position().ToString("X") + " " + entryCount.ToString("X"));

                    for (int i = 0; i < entryCount; i++)
                    {
                        FileEntry value = new FileEntry();

                        value.hash = streamReader.Read<ulong>();
                        value.compressedSize = streamReader.Read<uint>();
                        value.offset = streamReader.Read<uint>();
                        value.uncompressedSize = streamReader.Read<uint>();
                        table.Add(value);
                        if(i == 0 || i == 1)
                        {
                            Console.WriteLine(streamReader.Position().ToString("X"));
                        }
                    }
                    long tableEnd = streamReader.Position();
                    Console.WriteLine(tableEnd.ToString("X"));
                    int offsetThing = 0;
                    foreach (FileEntry entry in table)
                    {
                        var data = streamReader.ReadBytes(entry.offset + tableEnd + offsetThing, (int)entry.compressedSize - offsetThing);
                        if(BitConverter.ToUInt32(data, 0) == 0xEC30A437)
                        {
                            zstdDictHash = entry.hash;
                            zstdDict = data;
                            continue;
                        }
                        storeDict.Add(entry.hash, data);
                    }
                }

                var outputDirectory = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outputDirectory + "_");
                Directory.CreateDirectory(outputDirectory + "_\\KnownNames");
                Directory.CreateDirectory(outputDirectory + "_\\UnknownNames");
                Debugger.Launch();
                using (StreamWriter fpkList = new StreamWriter(outputDirectory + ".txt"))
                {
                    DecompressionOptions opt = new DecompressionOptions(zstdDict);
                    Decompressor dec = new Decompressor(opt);
                    for (int i = 0; i < table.Count; i++)
                    {
                        var entry = table[i];
                        string fileOutput;
                        if (entry.hash == zstdDictHash)
                        {
                            continue;
                        }
                        string name;
                        if (fileDict.ContainsKey(entry.hash))
                        {
                            name = fileDict[entry.hash];
                            if (name == "object_preset_list.lua" || name == "object_season.lua")
                            {
                                var a = 0;
                            }
                            fileOutput = Path.Combine(outputDirectory + "_\\KnownNames", $"{name}");
                        }
                        else
                        {
                            name = entry.hash.ToString("X") + ".lua";
                            fileOutput = Path.Combine(outputDirectory + "_\\UnknownNames", $"{name}");
                        } 
                        
                        byte[] compressedFile = storeDict[entry.hash];
                        byte[] uncompressedFile = dec.Unwrap(compressedFile, (int)entry.uncompressedSize);
                        if(uncompressedFile != null)
                        {
                            try
                            {
                                using (MemoryStream memStream = new MemoryStream())
                                {
                                    var luaStream = new MemoryStream(uncompressedFile);
                                    var header = new BHeader(luaStream);
                                    LFunction lfunction = header.Function.Parse(luaStream, header);
                                    var decompiler = new Decompiler(lfunction);
                                    decompiler.Decompile();
                                    using (var writer = new StreamWriter(memStream, new UTF8Encoding(false)))
                                    {
                                        decompiler.Print(new Output(writer));
                                        writer.Flush();
                                    }
                                    File.WriteAllBytes(fileOutput, memStream.ToArray());
                                }
                            }
                            catch
                            {
                                File.WriteAllBytes(fileOutput, uncompressedFile);
                            }
                        }
                        
                        fpkList.WriteLine(entry.hash.ToString("X") + "  " + name);
                    }
                }
            }
        }

        static void PackFpk(string dirPath)
        {
            var refText = dirPath.Substring(0, dirPath.Count() - 1) + ".txt";
            var fileDict = new Dictionary<ulong, byte[]>();

            //Get name references and file data; error if the file isn't there
            if (File.Exists(refText))
            {
                Console.WriteLine(Path.GetFileName(refText) + " exists");
                using (StreamReader reader = File.OpenText(refText))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] fileKey = line.Split(' ');
                        if (File.Exists(Path.Combine(dirPath, fileKey[1])))
                        {
                            fileDict.Add(ulong.Parse(fileKey[0], System.Globalization.NumberStyles.HexNumber), File.ReadAllBytes(Path.Combine(dirPath, fileKey[1])));
                        }
                        else
                        {
                            Console.WriteLine("Error: File " + fileKey[1] + " does not exist in directory!");
                            Console.ReadKey();
                            return;
                        }
                    }

                }
            }
            else
            {
                Console.WriteLine("Error: Directory dictionary " + Path.GetFileName(refText) + " does not exist!");
                Console.ReadKey();
                return;
            }
            Console.WriteLine("Proceeding...");
            //Write fpk
            var fpkMem = new MemoryStream();

            //Write header
            var fileHeader = new FpkHeader();
            fileHeader.fpk = 7041126; fileHeader.zero1 = 0; fileHeader.zero2 = 0; fileHeader.count = (uint)fileDict.Count;

            var fileHeaderBytes = Reloaded.Memory.Struct.GetBytes(ref fileHeader, true);
            fpkMem.Write(fileHeaderBytes, 0, fileHeaderBytes.Count());

            uint offset = 0;
            foreach (var f in fileDict)
            {
                var entry = new FileEntry();
                entry.hash = f.Key;
                entry.compressedSize = (uint)f.Value.Count();
                entry.offset = offset;
                entry.uncompressedSize = 0;
                offset += (uint)f.Value.Count();

                //Pad to multiple of 4
                var offPadding = 4 - (offset % 4);
                if (offPadding < 4)
                {
                    offset += offPadding;
                }

                var entryBytes = Reloaded.Memory.Struct.GetBytes(ref entry, true);
                fpkMem.Write(entryBytes, 0, entryBytes.Count());
            }

            //Write luas
            int counter = 0;
            foreach (var f in fileDict)
            {
                fpkMem.Write(f.Value, 0, f.Value.Count());
                //Pad to a multiple of 4
                var padding = 4 - (fpkMem.Position % 4);
                if (padding < 4 && counter < fileDict.Count() - 1)
                {
                    for (int i = 0; i < padding; i++)
                    {
                        fpkMem.WriteByte(0);
                    }
                }
                counter++;
            }

            //Write to file
            var newFile = dirPath + "new.ice";
            File.WriteAllBytes(newFile, fpkMem.ToArray());
        }

        static void CompareLua(string noNamePath, string namePath)
        {
            var filesNoName = Directory.GetFiles(noNamePath);
            var filesName = Directory.GetFiles(namePath);
            var NoDict = new Dictionary<uint, string>();
            var NameDict = new Dictionary<uint, string>();

            //Gather names and hashes
            foreach (var f in filesNoName)
            {
                var fTest = Extensions.Data.XXHash.XXH32(File.ReadAllBytes(f), 0);
                if (NoDict.ContainsKey(fTest))
                {
                    Console.WriteLine("Duplicate Found");
                    Console.WriteLine(f);
                    Console.WriteLine(NoDict[fTest]);
                    NoDict[fTest] = NoDict[fTest] + " " + Path.GetFileName(f); //Record duplicate names so we can have some idea of what they might be
                }
                else
                {
                    NoDict.Add(fTest, Path.GetFileName(f));
                }

            }
            foreach (var f in filesName)
            {
                var fTest = Extensions.Data.XXHash.XXH32(File.ReadAllBytes(f), 0);
                if (NameDict.ContainsKey(fTest))
                {
                    Console.WriteLine("Duplicate Found");
                    Console.WriteLine(f);
                    Console.WriteLine(NameDict[fTest]);
                    NameDict[fTest] = Path.GetFileNameWithoutExtension(NameDict[fTest]) + "_OR_" + Path.GetFileName(f); //Record duplicate names so we can have some idea of what they might be
                }
                else
                {
                    NameDict.Add(fTest, Path.GetFileName(f));
                }
            }

            //Compare hashes and write back with applied names if applicable
            var refText = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase).Substring(6), "FpkTool_Named.txt");
            using (StreamWriter fpkList = new StreamWriter(refText))
            {
                foreach (var f in NoDict)
                {
                    var name = NoDict[f.Key];
                    if (NameDict.ContainsKey(f.Key))
                    {
                        name = NameDict[f.Key];
                    }
                    var fileList = NoDict[f.Key].Split(' ');
                    if (fileList.Count() > 1)
                    {
                        for (int i = 0; i < fileList.Count(); i++)
                        {
                            fpkList.WriteLine(Path.GetFileNameWithoutExtension(fileList[i]) + " " + i.ToString() + name);
                        }
                    }
                    else
                    {
                        fpkList.WriteLine(Path.GetFileNameWithoutExtension(fileList[0]) + " " + name);
                    }
                }
            }
        }
    }
}
