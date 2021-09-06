using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BinaryEscaper
{
    class Program
    {
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

        static void EncodePNG(WorkOptions options)
        {
            Console.WriteLine("Opening file");

            byte[] rawBinary = File.ReadAllBytes(options.inputPath);
            byte[] binary;

            if (options.compress)
            {
                Console.WriteLine("Compressing data");

                binary = Compress(rawBinary);

                Console.WriteLine($"Compression done, ratio {Math.Round((float)binary.Length / rawBinary.Length * 100f)}%");
            }
            else
            {
                binary = rawBinary;
            }

            var imageSize = (int)Math.Ceiling(Math.Sqrt(binary.Length));

            Console.WriteLine($"Image size: {imageSize}");
            Console.WriteLine("Writing pixels now...");

            using (var image = new DirectBitmap(imageSize, imageSize))
            {
                Parallel.For(0, imageSize, y =>
                {
                    for(var x = 0; x < imageSize; x++)
                    {
                        var i = x + imageSize * y;
                        if (i >= binary.Length) break;

                        var byteValue = binary[i];

                        image.SetPixel(x, y, Color.FromArgb(255, byteValue, byteValue, byteValue));
                    }
                });

                Console.WriteLine("Done! Writing to file now");

                image.Bitmap.Save(options.outputPath, ImageFormat.Png);
            }
        }

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

            var bitmap = new Bitmap(options.inputPath);

            byte[] bitmapBytes = BitmapToByteArray(bitmap);
            var bytes = new List<byte>(bitmapBytes.Length / 4);

            Console.WriteLine("Sorting data...");

            for (var i = 0; i < bitmapBytes.Length / 4; i++)
            {
                if (bitmapBytes[i * 4 + 3] == 0) break;

                bytes.Add(bitmapBytes[i * 4]);
            }

            var bytesArray = bytes.ToArray();

            if (options.compress)
            {
                Console.WriteLine("Decompressing");
                bytesArray = Decompress(bytesArray);
            }

            Console.WriteLine("Done! Writing to output!");
            File.WriteAllBytes(options.outputPath, bytesArray);
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
            Console.WriteLine("-nocompress: disables compression/decompression");
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
                        }
                    }
                    else
                    {
                        ShowHelp();
                        return;
                    }
                }

                if(options.outputPath == "")
                {
                    if(options.operation == WorkOptions.Operation.Encode)
                    {
                        options.outputPath = Path.GetFileNameWithoutExtension(options.inputPath) + ".png";
                    }
                    else
                    {
                        options.outputPath = Path.GetFileNameWithoutExtension(options.inputPath) + ".out";
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
