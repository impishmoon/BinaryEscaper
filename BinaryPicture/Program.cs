using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BinaryEscaper
{
    class Program
    {
        //Function to gZip compress bytes
        static byte[] Compress(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    msi.CopyTo(gs);
                }

                return mso.ToArray();
            }
        }

        //Function to gZip decompress bytes
        static byte[] Decompress(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    gs.CopyTo(mso);
                }

                return mso.ToArray();
            }
        }

        //Function to get if we can write to a folder or not...
        static bool IsDirectoryWritable(string dirPath, bool throwIfFails = false)
        {
            try
            {
                using (FileStream fs = File.Create(
                    Path.Combine(
                        dirPath,
                        Path.GetRandomFileName()
                    ),
                    1,
                    FileOptions.DeleteOnClose)
                )
                { }
                return true;
            }
            catch
            {
                if (throwIfFails)
                    throw;
                else
                    return false;
            }
        }

        public static void PrintByteArray(byte[] array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                Console.Write($"{array[i]:X2}");
            }
            Console.WriteLine();
        }

        static string SHA256CheckSum(byte[] binary)
        {
            using(SHA256 sha = SHA256.Create())
            {
                var shaBinary = sha.ComputeHash(binary);
                string result = "";

                for (int i = 0; i < shaBinary.Length; i++)
                {
                    result += $"{shaBinary[i]:X2}";
                }

                return result.ToLower();
            }
        }

        //Soft get an index from a list. If index doesn't exist, return a failSafe
        static byte TryGetFromList(List<byte> array, int index, byte failSafe)
        {
            if(index < array.Count)
            {
                return array[index];
            }
            else
            {
                return failSafe;
            }
        }

        static void EncodePNG(WorkOptions options)
        {
            Console.WriteLine("Opening file");

            byte[] rawBinary = File.ReadAllBytes(options.inputPath);
            List<byte> binary;

            if (options.compress)
            {
                Console.WriteLine("Compressing data");

                binary = Compress(rawBinary).ToList();

                Console.WriteLine($"Compression done, ratio {Math.Round((float)binary.Count / rawBinary.Length * 100f)}%");
            }
            else
            {
                binary = rawBinary.ToList();
            }

            //Begin inserting header
            int headerInsertOffset = 0;

            //Was compression used in generating this PNG?
            binary.Insert(headerInsertOffset, options.compress ? (byte)1 : (byte)0);
            headerInsertOffset += 1;

            //Insert original extension string
            var extensionBytes = Encoding.UTF8.GetBytes(Path.GetExtension(options.inputPath).Substring(1)).ToList();
            extensionBytes.RemoveAll(x => x == 0);
            binary.InsertRange(headerInsertOffset, extensionBytes);
            headerInsertOffset += extensionBytes.Count;

            //Null-byte terminator
            binary.Insert(headerInsertOffset, 0);
            headerInsertOffset += 1;

            Console.WriteLine("Calculating SHA256 CheckSum...");
            string binaryCheckSum = SHA256CheckSum(rawBinary);
            for (int i = 0; i < binaryCheckSum.Length; i++)
            {
                var c = binaryCheckSum[i];

                binary.Insert(headerInsertOffset, (byte)c);
                headerInsertOffset += 1;
            }
            Console.Write("CheckSum: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(binaryCheckSum);
            Console.ForegroundColor = ConsoleColor.White;

            var imageSize = (int)Math.Ceiling(Math.Sqrt(binary.Count / 4));

            Console.WriteLine($"Image size: {imageSize}");
            Console.WriteLine("Writing pixels now...");

            //Insert all bytes into packed pixels on image, using multi-threading for speed
            using (var image = new DirectBitmap(imageSize, imageSize))
            {
                Parallel.For(0, imageSize, y =>
                {
                    for(var x = 0; x < imageSize; x++)
                    {
                        var i = (x + imageSize * y) * 4;

                        byte a, r, g, b;

                        if (i > binary.Count)
                        {
                            a = 255;
                            r = 255;
                            g = 255;
                            b = 255;
                        }
                        else
                        {
                            a = TryGetFromList(binary, i, 255);
                            r = TryGetFromList(binary, i + 1, 255);
                            g = TryGetFromList(binary, i + 2, 255);
                            b = TryGetFromList(binary, i + 3, 255);
                        }

                        image.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                    }
                });

                Console.WriteLine("Done! Writing to file now");

                var outputPath = options.outputPath;

                if(outputPath == "")
                {
                    outputPath = Path.GetFileNameWithoutExtension(options.inputPath) + ".png";

                    if (!IsDirectoryWritable(Directory.GetCurrentDirectory())) Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                }

                image.Bitmap.Save(outputPath, ImageFormat.Png);
            }
        }

        //Function that gets an image, returns array of bytes. Used by DecodePNG
        public static byte[] BitmapToByteArray(Bitmap bitmap)
        {
            BitmapData bmpdata = null;

            try
            {
                bmpdata = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                int numbytes = bmpdata.Stride * bitmap.Height;
                byte[] bytedata = new byte[numbytes];
                IntPtr ptr = bmpdata.Scan0;

                Marshal.Copy(ptr, bytedata, 0, numbytes);

                return bytedata;
            }
            finally
            {
                if (bmpdata != null)
                    bitmap.UnlockBits(bmpdata);
            }
        }

        static void DecodePNG(WorkOptions options)
        {
            Console.WriteLine("Opening file");

            //Open image
            var bitmap = new Bitmap(options.inputPath);

            //Get original bytes
            byte[] bitmapBytes = BitmapToByteArray(bitmap);

            var bytes = new List<byte>(bitmapBytes.Length / 4);

            Console.WriteLine("Sorting data...");

            //Since the bytes come in a wacky PNG order format, we resort them into the same order we put them in so it's easier to use them
            for (var i = 0; i < bitmapBytes.Length / 4; i++)
            {
                byte b = bitmapBytes[i * 4 + 0];
                byte g = bitmapBytes[i * 4 + 1];
                byte r = bitmapBytes[i * 4 + 2];
                byte a = bitmapBytes[i * 4 + 3];

                bytes.Add(a);
                bytes.Add(r);
                bytes.Add(g);
                bytes.Add(b);
            }

            //Trim all FF bytes at the end. We keep going until we see a byte that isnt FF
            //This is a bit problematic since we could accidentally delete a real FF byte, used by the original file,
            //but I hope the chances of that happening are low enough...
            while(bytes.Count > 0)
            {
                var i = bytes.Count - 1;

                if (bytes[i] == 255)
                {
                    bytes.RemoveAt(i);
                }
                else
                {
                    break;
                }
            }

            //Parse header
            var usedCompression = bytes[0] == 1;
            var originalExtensionBytes = new List<byte>();
            var originalExtension = "";

            //Read the extension string until we hit the null-byte terminator
            var scanExtensionIndex = 1;
            while (true)
            {
                if (bytes[scanExtensionIndex] == 0) break;

                originalExtensionBytes.Add(bytes[scanExtensionIndex]);
                scanExtensionIndex++;
            }
            originalExtension = Encoding.UTF8.GetString(originalExtensionBytes.ToArray());

            var originalCheckSumBytes = new List<byte>();
            var originalCheckSum = "";

            for(int i = 0; i < 64; i++)
            {
                originalCheckSumBytes.Add(bytes[i + 2 + originalExtensionBytes.Count]);
            }
            originalCheckSum = Encoding.ASCII.GetString(originalCheckSumBytes.ToArray());

            //Calculate total header size
            var headerSize = originalExtensionBytes.Count + 2 + 64; //+2 to account for usedCompression byte and null terminator byte, +64 for the sha256 checksum

            //Remove header and send it off to get decompressed/written to output
            bytes.RemoveRange(0, headerSize);
            var bytesArray = bytes.ToArray();

            if (usedCompression)
            {
                Console.WriteLine("Decompressing");
                bytesArray = Decompress(bytesArray);
            }

            Console.WriteLine("Calculating new SHA256 CheckSum...");
            var newCheckSum = SHA256CheckSum(bytesArray);

            if(originalCheckSum == newCheckSum)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("CheckSums match!");
                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine("CheckSum is: " + originalCheckSum);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("CheckSums do not match!");

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Original CheckSum: ");
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine(originalCheckSum);

                Console.ForegroundColor = ConsoleColor.White;

                Console.Write("New CheckSum: ");
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine(newCheckSum);
                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine("Press any key to continue decoding...");
                Console.ReadKey();
            }

            var outputPath = options.outputPath;
            if(outputPath == "")
            {
                //If output path was no specified, we use the name of the file plus the original extension string found in our header
                outputPath = Path.GetFileNameWithoutExtension(options.inputPath) + "." + originalExtension;

                if (!IsDirectoryWritable(Directory.GetCurrentDirectory())) Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            }

            Console.WriteLine("Done! Writing to output!");
            File.WriteAllBytes(outputPath, bytesArray);
        }

        static void ShowHelp()
        {
            Console.WriteLine("Example:");
            Console.WriteLine("  BinaryEscaper.exe -png -e -in target.zip");
            Console.WriteLine("  BinaryEscaper.exe -png -d -in target.png");
            Console.WriteLine("");
            Console.WriteLine("-in: specify input file to target");
            Console.WriteLine("-out: specify output file (optional, if not specified, a default name is assumed)");
            Console.WriteLine("-encode [-e]: encode a file");
            Console.WriteLine("-decode [-d]: decode a file");
            Console.WriteLine("-nocompress: disables compression/decompression (this is ignored in decoding mode)");
        }

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                var options = new WorkOptions();

                for (int i = 0; i < args.Length; i++)
                {
                    var arg = args[i];

                    if (arg.StartsWith("-"))
                    {
                        //settings

                        if((arg == "-e" || arg == "-encode"))
                        {
                            options.operation = WorkOptions.Operation.Encode;
                        }
                        else if ((arg == "-d" || arg == "-decode"))
                        {
                            options.operation = WorkOptions.Operation.Decode;
                        }
                        else if(arg == "-nocompress")
                        {
                            options.compress = false;
                        }
                        else if(arg == "-in" && args.Length > i + 1)
                        {
                            options.inputPath = args[i + 1];
                        }
                        else if (arg == "-out" && args.Length > i + 1)
                        {
                            options.outputPath = args[i + 1];
                        }
                    }
                }

                if (!options.IsValid())
                {
                    var inputPath = string.Join(" ", args);

                    if (File.Exists(inputPath))
                    {
                        if (inputPath.EndsWith(".png"))
                        {
                            options.inputPath = inputPath;
                            options.operation = WorkOptions.Operation.Decode;
                        }
                        else
                        {
                            options.inputPath = inputPath;
                            options.operation = WorkOptions.Operation.Encode;
                            options.compress = !inputPath.EndsWith(".zip") && !inputPath.EndsWith(".rar") && !inputPath.EndsWith(".gz");
                        }
                    }
                    else
                    {
                        ShowHelp();
                        return;
                    }
                }

                if (options.operation == WorkOptions.Operation.Encode) EncodePNG(options);
                if (options.operation == WorkOptions.Operation.Decode) DecodePNG(options);
            }
            else
            {
                ShowHelp();
            }
        }
    }
}
