using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Common;

namespace ExtractMod
{
    class Program
    {
        static string version;

        static void Main(string[] args)
        {
            try
            {
                string moddedfolder = Path.Combine(ClientSettings.AssetsPath, "assets"); //"C:\\Users\\tyron\\AppData\\Roaming\\Vintagestory\\assets"; //

                version = GameVersion.ShortGameVersion;
                if (args.Length > 0) version = args[0];

                Console.WriteLine("Baseline version is " + version);
                Console.WriteLine("Modded folder is " + moddedfolder);

                string vanillafolder = Path.Combine(Path.GetTempPath(), "VintageStory", version);

                EnsureAssetsAvailable(vanillafolder);
                List<string> relPaths = ExtractChanges(vanillafolder, moddedfolder);

                if (relPaths.Count == 0)
                {
                    Console.WriteLine("No differences detected! Aborting mod creation.");
                    return;
                }

                Console.Write("Please enter a name for your mod: ");
                string name = Console.ReadLine();

                Console.Write("Please enter an author name for your mod: ");
                string author = Console.ReadLine();

                Console.WriteLine("Thanks! Generating modinfo and copying modified files...");

                string outfolder = Path.Combine(Path.GetTempPath(), "vsmodextract");
                if (Directory.Exists(outfolder)) Directory.Delete(outfolder, true);

                string modid = name.Replace(" ", "");

                ModInfo info = new ModInfo()
                {
                    Name = name,
                    ModID = modid,
                    Type = EnumModType.Content,
                    Authors = new string[] { author }
                };

                Directory.CreateDirectory(outfolder);
                File.WriteAllText(Path.Combine(outfolder, "modinfo.json"), JsonConvert.SerializeObject(info, Formatting.Indented));

                string assetsfolder = Path.Combine(outfolder, "assets", "game");
                Directory.CreateDirectory(assetsfolder);

                foreach (string relpath in relPaths)
                {
                    string outfile = Path.Combine(assetsfolder, relpath);
                    Directory.CreateDirectory(Path.GetDirectoryName(outfile));
                    File.Copy(Path.Combine(moddedfolder, relpath), outfile);
                }

                string archivepath = Path.Combine(GamePaths.Mods, modid + ".zip");
                Console.WriteLine("Done. Packing all into mod archive {0}...", archivepath);

                CreateModArchive(archivepath, outfolder);


                Console.WriteLine("Archive created. Congratulations, you created a mod! \\o/");

                Console.WriteLine("The mod is already in your mods folder, so feel free to install an new version of the game now. Hit enter to exit.");

            } catch (Exception e)
            {
                Console.WriteLine("Exception thrown trying to extract a mod :<\n{0}", e);

                
            }


            Console.ReadLine();
        }

        private static List<string> ExtractChanges(string vanillafolder, string moddedfolder)
        {
            List<string> differentFiles = new List<string>();

            foreach (string file in GetFiles(moddedfolder))
            {
                if (!file.EndsWith(".json")) continue;

                string relPath = file.Substring(moddedfolder.Length + 1);
                string otherfile = Path.Combine(vanillafolder, "assets", relPath);

                if (!File.Exists(otherfile)) continue;

                string moddedjson = File.ReadAllText(file).Replace("\r", "").Replace("\n", "");
                string vanillajson = File.ReadAllText(otherfile).Replace("\r", "").Replace("\n", "");

                if (!moddedjson.Equals(vanillajson))
                {
                    Console.WriteLine(relPath + " is different.");
                    differentFiles.Add(relPath);
                }
            }

            return differentFiles;
        }



        static IEnumerable<string> GetFiles(string path)
        {
            Queue<string> queue = new Queue<string>();
            queue.Enqueue(path);
            while (queue.Count > 0)
            {
                path = queue.Dequeue();
                try
                {
                    foreach (string subDir in Directory.GetDirectories(path))
                    {
                        queue.Enqueue(subDir);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
                string[] files = null;
                try
                {
                    files = Directory.GetFiles(path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        yield return files[i];
                    }
                }
            }
        }

        private static void EnsureAssetsAvailable(string vanillafolder)
        {
            if (Directory.Exists(Path.Combine(vanillafolder, "assets")))
            {
                Console.WriteLine("Found vanilla assets in temp folder, will use that as reference.");
            }
            else
            {
                Console.WriteLine("Downloading vanilla assets for v{0}...", version);
                string filename = GameVersion.Branch == EnumGameBranch.Stable ? "/files/stable/vs_server_" + version + ".tar.gz" : "/files/unstable/vs_server_" + version + ".tar.gz";
                Directory.CreateDirectory(vanillafolder);

                var zipfile = Path.Combine(vanillafolder, version) + ".tar.gz";

                try
                {
                    WebClient wclient = new WebClient();
                    wclient.DownloadFile("https://account.vintagestory.at" + filename, zipfile);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed downloading " + filename + ". Giving up, sorry. Exception: {0}", e);
                    Directory.Delete(vanillafolder);
                    return;
                }

                Console.WriteLine("File downloaded. Extracting...");
                var zip = new FastZip();
                ExtractTGZ(zipfile, vanillafolder);

                Console.WriteLine("Files extracted...");
            }
        }


        public static void ExtractTGZ(String gzArchiveName, String destFolder)
        {
            Stream inStream = File.OpenRead(gzArchiveName);
            Stream gzipStream = new GZipInputStream(inStream);

            TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream);
            tarArchive.ExtractContents(destFolder);
            tarArchive.Close();

            gzipStream.Close();
            inStream.Close();
        }



        public static void CreateModArchive(string outPathname, string folderName)
        {
            FileStream fsOut = File.Create(outPathname);
            ZipOutputStream zipStream = new ZipOutputStream(fsOut);

            zipStream.SetLevel(3); //0-9, 9 being the highest level of compression

            // This setting will strip the leading part of the folder path in the entries, to
            // make the entries relative to the starting folder.
            // To include the full path for each entry up to the drive root, assign folderOffset = 0.
            int folderOffset = folderName.Length + (folderName.EndsWith("\\") ? 0 : 1);

            CompressFolder(folderName, zipStream, folderOffset);

            zipStream.IsStreamOwner = true; // Makes the Close also Close the underlying stream
            zipStream.Close();
        }



        public static void CompressFolder(string path, ZipOutputStream zipStream, int folderOffset)
        {
            string[] files = Directory.GetFiles(path);

            foreach (string filename in files)
            {
                FileInfo fi = new FileInfo(filename);

                string entryName = filename.Substring(folderOffset); // Makes the name in zip based on the folder
                entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
                ZipEntry newEntry = new ZipEntry(entryName);
                newEntry.DateTime = fi.LastWriteTime; // Note the zip format stores 2 second granularity


                // To permit the zip to be unpacked by built-in extractor in WinXP and Server2003, WinZip 8, Java, and other older code,
                // you need to do one of the following: Specify UseZip64.Off, or set the Size.
                // If the file may be bigger than 4GB, or you do not need WinXP built-in compatibility, you do not need either,
                // but the zip will be in Zip64 format which not all utilities can understand.
                //   zipStream.UseZip64 = UseZip64.Off;
                newEntry.Size = fi.Length;

                zipStream.PutNextEntry(newEntry);

                // Zip the file in buffered chunks
                // the "using" will close the stream even if an exception occurs
                byte[] buffer = new byte[4096];
                using (FileStream streamReader = File.OpenRead(filename))
                {
                    StreamUtils.Copy(streamReader, zipStream, buffer);
                }
                zipStream.CloseEntry();
            }
            string[] folders = Directory.GetDirectories(path);
            foreach (string folder in folders)
            {
                CompressFolder(folder, zipStream, folderOffset);
            }
        }


    }
}
