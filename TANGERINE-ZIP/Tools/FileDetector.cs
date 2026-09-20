using System;
using System.IO;

namespace TANGERINE_ZIP.Tools
{
    internal class FileDetector
    {
        public enum FileType
        {
            Unknown = 0,

            // Archive and compression formats
            Zip = 1,
            Rar = 2,
            SevenZip = 3,
            Tar = 4,
            GZip = 5,
            BZip2 = 6,
            Xz = 7,
            Lz4 = 8,
            Zstd = 9,

            // Disk image formats
            Iso = 20,
            Wim = 21,
            Vhd = 22,
            Vhdx = 23,
            Dmg = 24,

            // Executable formats
            Exe = 40,
            Elf = 41,
            JavaClass = 42,

            // Image formats
            Png = 60,
            Jpeg = 61,
            Gif = 62,
            Bmp = 63,
            Tiff = 64,
            WebP = 65,
            Ico = 66,

            // Audio formats
            Mp3 = 80,
            Wav = 81,
            Flac = 82,
            Ogg = 83,

            // Video formats
            Mp4 = 100,
            Avi = 101,
            Mkv = 102,
            WebM = 103,

            // Document formats
            Pdf = 120,
            Rtf = 121,

            // Office Open XML formats (ZIP containers)
            Docx = 140,
            Xlsx = 141,
            Pptx = 142,

            // Database formats
            Sqlite = 160
        }
        public static bool IsCompressedFile(string path)
        {
            FileType type = DetectFileType(path);

            return type switch
            {
                FileType.Zip or
                FileType.Rar or
                FileType.SevenZip or
                FileType.Tar or
                FileType.GZip or
                FileType.BZip2 or
                FileType.Xz or
                FileType.Lz4 or
                FileType.Zstd or
                FileType.Iso or
                FileType.Wim => true,

                _ => false
            };
        }
        public static FileType DetectFileType(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return FileType.Unknown;
            }

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return DetectFileType(stream);
        }

        public static FileType DetectFileType(Stream stream)
        {
            const int BufferSize = 36864;
            byte[] buffer = new byte[BufferSize];
            long originalPosition = stream.CanSeek ? stream.Position : 0;
            int read = stream.Read(buffer, 0, BufferSize);
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }

            ReadOnlySpan<byte> h = buffer.AsSpan(0, read);


            // =========================
            // Archive and compression formats
            // =========================


            // ZIP local header, empty archive, or spanned archive
            if (Match(h, 0x50, 0x4B, 0x03, 0x04) ||
                Match(h, 0x50, 0x4B, 0x05, 0x06) ||
                Match(h, 0x50, 0x4B, 0x07, 0x08))
                return FileType.Zip;


            // RAR
            if (Match(h,
                0x52, 0x61, 0x72,
                0x21, 0x1A, 0x07))
                return FileType.Rar;


            // 7Z
            if (Match(h,
                0x37, 0x7A, 0xBC,
                0xAF, 0x27, 0x1C))
                return FileType.SevenZip;


            // GZIP
            if (Match(h,
                0x1F, 0x8B, 0x08))
                return FileType.GZip;


            // BZIP2
            if (MatchAscii(h, "BZh"))
                return FileType.BZip2;


            // XZ
            if (Match(h,
                0xFD, 0x37, 0x7A,
                0x58, 0x5A, 0x00))
                return FileType.Xz;


            // ZSTD
            if (Match(h,
                0x28, 0xB5, 0x2F, 0xFD))
                return FileType.Zstd;


            // LZ4
            if (Match(h,
                0x04, 0x22, 0x4D, 0x18))
                return FileType.Lz4;

            // TAR is accepted by its POSIX signature or by a valid header checksum.
            if ((h.Length >= 262 && MatchAscii(h.Slice(257), "ustar")) || HasValidTarHeader(h))
                return FileType.Tar;



            // =========================
            // Disk image formats
            // =========================


            // ISO9660
            if (h.Length > 0x8006 &&
                h[0x8001] == 'C' &&
                h[0x8002] == 'D' &&
                h[0x8003] == '0' &&
                h[0x8004] == '0' &&
                h[0x8005] == '1')
                return FileType.Iso;


            // WIM
            if (MatchAscii(h, "MSWIM"))
                return FileType.Wim;


            // VHD
            if (h.Length > 512 &&
                MatchAscii(h.Slice(0x100), "conectix"))
                return FileType.Vhd;


            // VHDX
            if (MatchAscii(h, "vhdxfile"))
                return FileType.Vhdx;


            // DMG
            if (h.Length > 4 &&
                h[0] == 0x78 &&
                h[1] == 0x01)
                return FileType.Dmg;



            // =========================
            // Executable formats
            // =========================


            // Windows EXE/DLL
            if (Match(h, 0x4D, 0x5A))
                return FileType.Exe;


            // Linux ELF
            if (Match(h,
                0x7F, 0x45,
                0x4C, 0x46))
                return FileType.Elf;


            // Java Class
            if (Match(h,
                0xCA, 0xFE,
                0xBA, 0xBE))
                return FileType.JavaClass;



            // =========================
            // Image formats
            // =========================


            if (Match(h,
                0x89, 0x50,
                0x4E, 0x47))
                return FileType.Png;


            if (Match(h,
                0xFF, 0xD8, 0xFF))
                return FileType.Jpeg;


            if (MatchAscii(h, "GIF"))
                return FileType.Gif;


            if (Match(h, 0x42, 0x4D))
                return FileType.Bmp;


            if (Match(h,
                0x49, 0x49,
                0x2A, 0x00)
                ||
                Match(h,
                0x4D, 0x4D,
                0x00, 0x2A))
                return FileType.Tiff;


            if (MatchAscii(h, "RIFF") &&
                h.Length > 12 &&
                h[8] == 'W' &&
                h[9] == 'E' &&
                h[10] == 'B' &&
                h[11] == 'P')
                return FileType.WebP;


            if (Match(h,
                0x00, 0x00,
                0x01, 0x00))
                return FileType.Ico;



            // =========================
            // Audio formats
            // =========================


            if (MatchAscii(h, "ID3"))
                return FileType.Mp3;


            if (MatchAscii(h, "fLaC"))
                return FileType.Flac;


            if (MatchAscii(h, "OggS"))
                return FileType.Ogg;


            if (MatchAscii(h, "RIFF") &&
                h.Length > 12 &&
                h[8] == 'W' &&
                h[9] == 'A' &&
                h[10] == 'V' &&
                h[11] == 'E')
                return FileType.Wav;



            // =========================
            // Video formats
            // =========================


            // MP4/MOV
            if (h.Length > 12 &&
                h[4] == 'f' &&
                h[5] == 't' &&
                h[6] == 'y' &&
                h[7] == 'p')
                return FileType.Mp4;


            if (MatchAscii(h, "RIFF") &&
                h.Length > 12 &&
                h[8] == 'A' &&
                h[9] == 'V' &&
                h[10] == 'I')
                return FileType.Avi;


            if (MatchAscii(h, "1A45DFA3"))
                return FileType.Mkv;


            if (MatchAscii(h, "RIFF"))
                return FileType.WebM;



            // =========================
            // Document formats
            // =========================


            if (MatchAscii(h, "%PDF"))
                return FileType.Pdf;


            if (MatchAscii(h, "{\\rtf"))
                return FileType.Rtf;



            // =========================
            // Database formats
            // =========================


            if (MatchAscii(h,
                "SQLite format 3"))
                return FileType.Sqlite;



            return FileType.Unknown;
        }

        public static FileType GetTypeFromCreateFilterIndex(int filterIndex)
        {
            return filterIndex switch
            {
                1 => FileType.Zip,
                2 => FileType.SevenZip,
                3 => FileType.Tar,
                4 => FileType.GZip,
                5 => FileType.BZip2,
                6 => FileType.Xz,
                7 => FileType.Lz4,
                8 => FileType.Zstd,
                9 => FileType.Iso,
                10 => FileType.Wim,
                11 => FileType.Rar,
                _ => FileType.Unknown
            };
        }

        private static bool HasValidTarHeader(ReadOnlySpan<byte> data)
        {
            const int HeaderSize = 512;
            const int ChecksumOffset = 148;
            const int ChecksumLength = 8;
            if (data.Length < HeaderSize || data[..HeaderSize].IndexOfAnyExcept((byte)0) < 0)
            {
                return false;
            }

            int storedChecksum = 0;
            for (int index = ChecksumOffset; index < ChecksumOffset + ChecksumLength; index++)
            {
                byte value = data[index];
                if (value is 0 or (byte)' ')
                {
                    continue;
                }
                if (value < '0' || value > '7')
                {
                    return false;
                }
                storedChecksum = (storedChecksum * 8) + value - '0';
            }

            int calculatedChecksum = 0;
            for (int index = 0; index < HeaderSize; index++)
            {
                calculatedChecksum += index >= ChecksumOffset && index < ChecksumOffset + ChecksumLength
                    ? (byte)' '
                    : data[index];
            }
            return storedChecksum == calculatedChecksum;
        }



        private static bool Match(ReadOnlySpan<byte> data, params byte[] sig)
        {
            if (data.Length < sig.Length)
                return false;

            for (int i = 0; i < sig.Length; i++)
            {
                if (data[i] != sig[i])
                    return false;
            }

            return true;
        }



        private static bool MatchAscii(ReadOnlySpan<byte> data, string text)
        {
            if (data.Length < text.Length)
                return false;


            for (int i = 0; i < text.Length; i++)
            {
                if (data[i] != text[i])
                    return false;
            }

            return true;
        }
    }
}
