using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Reloaded.Memory.Streams;

namespace FpkTool
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
            public int Hash;
            public int Length;
            public int Offset;
            public int Padding;
        }

        static void Main(string[] args)
        {
            if (args.Count() == 0)
            {
                PrintUsage();
            } else if (args.Count() == 2)
            {
                CompareLua(args[0], args[1]);
            } else
            {
                var path = Path.GetFullPath(args[0]);
                if (File.Exists(path))
                {
                    DumpFpk(path);
                } else if (Directory.Exists(path))
                {
                    PackFpk(path);
                } else
                {
                    PrintUsage();
                }
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("FpkTool");
            Console.WriteLine("usage: FpkTool <path to fpk>");
            Console.WriteLine("       FpkTool <path to extracted fpk folder>");
            Console.WriteLine("       FpkTool <path to unnamed fpk folder> <path to named fpk folder>");
            Console.WriteLine("ex. FpkTool.exe " + @"H:\pso2_bin\data\win32\00800bcb4e790060f5a47b85fbc2acd0");
        }

        static void DumpFpk(string filePath)
        {
            var fileDict = new Dictionary<int, string>();
            var storeDict = new Dictionary<int, byte[]>();
            var luaFiles = new List<string>();
            var refText = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase).Substring(6), "FpkTool.txt");

            //Get name references if they exist
            if (File.Exists(refText))
            {
                Console.WriteLine("FpkTool.txt exists");
                using (StreamReader reader = File.OpenText(refText))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] fileKey = line.Split(' ');
                        fileDict.Add(int.Parse(fileKey[0], System.Globalization.NumberStyles.HexNumber), fileKey[1]);
                    }

                }
            } else
            {
                Console.WriteLine("FpkTool.txt does not exist");
            }

            //Extract files and apply names if applicable
            Console.WriteLine("Proceeding...");
            if (File.Exists(filePath))
            {
                var fpk = File.ReadAllBytes(filePath);
                using (var fileStream = new FileStream(filePath, FileMode.Open))
                using (var streamReader = new BufferedStreamReader(fileStream, 8192))
                {
                    List<FileEntry> table = new List<FileEntry>();

                    streamReader.Seek(0xC, SeekOrigin.Begin);
                    streamReader.Read<UInt32>(out UInt32 entryCount);

                    for(int i = 0; i < entryCount;  i++)
                    {
                        streamReader.Read(out FileEntry value);
                        table.Add(value);
                    }
                    long tableEnd = streamReader.Position();

                    foreach (FileEntry entry in table)
                        storeDict.Add(entry.Hash, streamReader.ReadBytes(entry.Offset + tableEnd, (int)entry.Length));
                }

                var outputDirectory = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outputDirectory + "_");
                using (StreamWriter fpkList = new StreamWriter(outputDirectory + ".txt"))
                {
                    foreach (var file in storeDict)
                    {
                        string name;
                        if (fileDict.ContainsKey(file.Key))
                        {
                            name = fileDict[file.Key];
                        } else
                        {
                            name = file.Key.ToString("X") + ".lua";
                        }
                        var fileOutput = Path.Combine(outputDirectory + "_", $"{name}");
                        File.WriteAllBytes(fileOutput, file.Value);
                        fpkList.WriteLine(file.Key.ToString("X") + " " + name);
                    }
                }
            }
        }

        static void PackFpk(string dirPath)
        {
            var refText = dirPath.Substring(0, dirPath.Count()-1) + ".txt";
            var fileDict = new Dictionary<int, byte[]>();

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
                        if(File.Exists(Path.Combine(dirPath, fileKey[1])))
                        {
                            fileDict.Add(int.Parse(fileKey[0], System.Globalization.NumberStyles.HexNumber), File.ReadAllBytes(Path.Combine(dirPath, fileKey[1])));
                        } else
                        {
                            Console.WriteLine("Error: File " + fileKey[1] + " does not exist in directory!");
                            Console.ReadKey();
                            return;
                        }
                    }

                }
            } else
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

            var offset = 0;
            foreach (var f in fileDict)
            {
                var entry = new FileEntry();
                entry.Hash = f.Key;
                entry.Length = f.Value.Count(); 
                entry.Offset = offset;
                entry.Padding = 0;
                offset += f.Value.Count();

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
                if (padding < 4 && counter < fileDict.Count()-1)
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
            var filesName   = Directory.GetFiles(namePath);
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
                } else
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
                    } else
                    {
                        fpkList.WriteLine(Path.GetFileNameWithoutExtension(fileList[0]) + " " + name);
                    }
                }
            }
        }
    }
}
