using System;
using System.Collections.Generic;
using System.IO;

namespace Ryujinx.Graphics.Vulkan.FrameGen
{
    // NEOV2-FRAMEGEN1: le os shaders SPIR-V (recursos RCDATA) da Lossless.dll fornecida pelo usuario.
    // Comportamento de referencia: Eden (lossless_dll.cpp / lsfg_translate.cpp), reescrito para o Neo.
    public enum LosslessStatus
    {
        Ok = 0,
        NotInstalled = 1,
        UnreadableFile = 2,
        NotPortableExecutable = 3,
        MissingShaders = 4,
        TranslationFailed = 5,
    }

    public static class LosslessDll
    {
        public const uint MipmapsId = 255;
        public const uint GenerateId = 256;
        public const uint NativeFp16Offset = 49;

        public static readonly uint[] Alpha = { 290, 291, 292, 293 };
        public static readonly uint[] Beta = { 298, 299, 300, 301, 302 };
        public static readonly uint[] Gamma = { 280, 282, 283, 284, 285 };
        public static readonly uint[] Delta = { 280, 286, 287, 288, 289, 281, 294, 295, 296, 297 };

        private const uint SpirvMagic = 0x07230203;
        private const uint ResourceTypeRcData = 10;
        private const uint SubdirectoryFlag = 0x80000000;
        private const long MaxDllBytes = 256L * 1024 * 1024;

        private readonly struct Section
        {
            public readonly uint VirtualAddress, VirtualSize, RawAddress, RawSize;

            public Section(uint va, uint vs, uint ra, uint rs)
            {
                VirtualAddress = va;
                VirtualSize = vs;
                RawAddress = ra;
                RawSize = rs;
            }
        }

        private readonly struct Entry
        {
            public readonly uint Id, Offset;
            public readonly bool IsDirectory, IsNamed;

            public Entry(uint id, uint offset, bool isDirectory, bool isNamed)
            {
                Id = id;
                Offset = offset;
                IsDirectory = isDirectory;
                IsNamed = isNamed;
            }
        }

        public static IEnumerable<uint> RequiredIds()
        {
            yield return MipmapsId;
            yield return GenerateId;
            for (uint id = 280; id <= 302; id++)
            {
                yield return id;
            }
        }

        public static string Describe(LosslessStatus status) => status switch
        {
            LosslessStatus.Ok => "DLL 有效",
            LosslessStatus.NotInstalled => "未找到文件",
            LosslessStatus.UnreadableFile => "无法读取文件",
            LosslessStatus.NotPortableExecutable => "该文件不是有效的 Windows DLL",
            LosslessStatus.MissingShaders => "该 DLL 不包含预期的 Lossless Scaling 着色器",
            _ => "准备 DLL 着色器失败",
        };

        public static LosslessStatus Validate(string path)
        {
            return Load(path, out _);
        }

        /// <summary>Le a DLL e devolve os 25 modulos SPIR-V (variante FP16 nativa) com bindings renumerados.</summary>
        public static LosslessStatus Load(string path, out Dictionary<uint, uint[]> modules)
        {
            modules = null;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return LosslessStatus.NotInstalled;
            }

            byte[] image;
            try
            {
                if (new FileInfo(path).Length is <= 0 or > MaxDllBytes)
                {
                    return LosslessStatus.UnreadableFile;
                }

                image = File.ReadAllBytes(path);
            }
            catch
            {
                return LosslessStatus.UnreadableFile;
            }

            LosslessStatus status = ParseResources(image, out Dictionary<uint, (int Offset, int Size)> spans);
            if (status != LosslessStatus.Ok)
            {
                return status;
            }

            Dictionary<uint, uint[]> result = new();
            foreach (uint id in RequiredIds())
            {
                if (!spans.TryGetValue(id + NativeFp16Offset, out (int Offset, int Size) span))
                {
                    return LosslessStatus.MissingShaders;
                }

                uint[] words = AdoptSpirv(image, span.Offset, span.Size);
                if (words == null)
                {
                    return LosslessStatus.TranslationFailed;
                }

                result[id] = words;
            }

            modules = result;
            return LosslessStatus.Ok;
        }

        private static bool Read16(byte[] b, long o, out ushort v)
        {
            v = 0;
            if (o < 0 || o + 2 > b.Length)
            {
                return false;
            }

            v = BitConverter.ToUInt16(b, (int)o);
            return true;
        }

        private static bool Read32(byte[] b, long o, out uint v)
        {
            v = 0;
            if (o < 0 || o + 4 > b.Length)
            {
                return false;
            }

            v = BitConverter.ToUInt32(b, (int)o);
            return true;
        }

        private static LosslessStatus ParseResources(byte[] image, out Dictionary<uint, (int Offset, int Size)> resources)
        {
            resources = new Dictionary<uint, (int, int)>();

            if (!Read16(image, 0, out ushort dos) || dos != 0x5A4D ||
                !Read32(image, 0x3C, out uint peOffset) ||
                !Read32(image, peOffset, out uint peSig) || peSig != 0x00004550)
            {
                return LosslessStatus.NotPortableExecutable;
            }

            long optional = peOffset + 4 + 20;
            if (!Read16(image, optional, out ushort magic))
            {
                return LosslessStatus.NotPortableExecutable;
            }

            long dataDirectory = magic switch
            {
                0x010B => optional + 96,
                0x020B => optional + 112,
                _ => -1,
            };
            if (dataDirectory < 0 ||
                !Read16(image, peOffset + 4 + 2, out ushort sectionCount) ||
                !Read16(image, peOffset + 4 + 16, out ushort optionalSize))
            {
                return LosslessStatus.NotPortableExecutable;
            }

            long table = peOffset + 4 + 20 + optionalSize;
            List<Section> sections = new();
            for (int i = 0; i < sectionCount; i++)
            {
                long o = table + i * 40L;
                if (!Read32(image, o + 8, out uint vs) || !Read32(image, o + 12, out uint va) ||
                    !Read32(image, o + 16, out uint rs) || !Read32(image, o + 20, out uint ra))
                {
                    return LosslessStatus.NotPortableExecutable;
                }

                sections.Add(new Section(va, vs, ra, rs));
            }

            if (!Read32(image, dataDirectory + 2 * 8, out uint resourceRva) || resourceRva == 0)
            {
                return LosslessStatus.MissingShaders;
            }

            long resourceBase = RvaToOffset(sections, resourceRva);
            if (resourceBase < 0)
            {
                return LosslessStatus.NotPortableExecutable;
            }

            if (!ReadEntries(image, resourceBase, out List<Entry> types))
            {
                return LosslessStatus.MissingShaders;
            }

            foreach (Entry type in types)
            {
                if (type.IsNamed || type.Id != ResourceTypeRcData || !type.IsDirectory ||
                    !ReadEntries(image, resourceBase + type.Offset, out List<Entry> names))
                {
                    continue;
                }

                foreach (Entry name in names)
                {
                    if (name.IsNamed || !name.IsDirectory ||
                        !ReadEntries(image, resourceBase + name.Offset, out List<Entry> languages))
                    {
                        continue;
                    }

                    foreach (Entry language in languages)
                    {
                        if (language.IsDirectory)
                        {
                            continue;
                        }

                        long leaf = resourceBase + language.Offset;
                        if (!Read32(image, leaf, out uint rva) || !Read32(image, leaf + 4, out uint size) || size == 0)
                        {
                            continue;
                        }

                        long offset = RvaToOffset(sections, rva);
                        if (offset < 0 || offset + size > image.Length)
                        {
                            continue;
                        }

                        resources[name.Id] = ((int)offset, (int)size);
                        break;
                    }
                }
            }

            return LosslessStatus.Ok;
        }

        private static bool ReadEntries(byte[] image, long directory, out List<Entry> entries)
        {
            entries = new List<Entry>();
            if (!Read16(image, directory + 12, out ushort named) || !Read16(image, directory + 14, out ushort ids))
            {
                return false;
            }

            for (int i = 0; i < named + ids; i++)
            {
                long o = directory + 16 + i * 8L;
                if (!Read32(image, o, out uint name) || !Read32(image, o + 4, out uint data))
                {
                    return false;
                }

                entries.Add(new Entry(name & ~SubdirectoryFlag, data & ~SubdirectoryFlag,
                    (data & SubdirectoryFlag) != 0, (name & SubdirectoryFlag) != 0));
            }

            return true;
        }

        private static long RvaToOffset(List<Section> sections, uint rva)
        {
            foreach (Section s in sections)
            {
                uint span = Math.Max(s.VirtualSize, s.RawSize);
                if (span == 0 || rva < s.VirtualAddress)
                {
                    continue;
                }

                uint relative = rva - s.VirtualAddress;
                if (relative < span)
                {
                    return (long)s.RawAddress + relative;
                }
            }

            return -1;
        }

        // Copia o modulo e renumera os bindings em ordem continua por (set, binding), como o Eden.
        private static uint[] AdoptSpirv(byte[] image, int offset, int size)
        {
            if (size < 20 || size % 4 != 0)
            {
                return null;
            }

            uint[] words = new uint[size / 4];
            Buffer.BlockCopy(image, offset, words, 0, size);
            if (words[0] != SpirvMagic)
            {
                return null;
            }

            const uint OpFunction = 54, OpDecorate = 71, DecorationBinding = 33, DecorationSet = 34;
            Dictionary<uint, uint> sets = new();
            List<(uint Target, uint Binding, int Literal)> slots = new();

            int pos = 5;
            while (pos < words.Length)
            {
                uint length = words[pos] >> 16;
                uint opcode = words[pos] & 0xFFFF;
                if (length == 0 || pos + length > words.Length)
                {
                    return words;
                }

                if (opcode == OpFunction)
                {
                    break;
                }

                if (opcode == OpDecorate && length >= 4)
                {
                    if (words[pos + 2] == DecorationSet)
                    {
                        sets[words[pos + 1]] = words[pos + 3];
                    }
                    else if (words[pos + 2] == DecorationBinding)
                    {
                        slots.Add((words[pos + 1], words[pos + 3], pos + 3));
                    }
                }

                pos += (int)length;
            }

            List<(uint Set, uint Binding, int Literal, int Order)> ordered = new();
            for (int i = 0; i < slots.Count; i++)
            {
                sets.TryGetValue(slots[i].Target, out uint set);
                ordered.Add((set, slots[i].Binding, slots[i].Literal, i));
            }

            ordered.Sort((a, b) =>
            {
                int c = a.Set.CompareTo(b.Set);
                if (c != 0) return c;
                c = a.Binding.CompareTo(b.Binding);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                words[ordered[i].Literal] = (uint)i;
            }

            return words;
        }
    }
}
