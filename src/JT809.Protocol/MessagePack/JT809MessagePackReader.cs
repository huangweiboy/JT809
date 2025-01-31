﻿using JT809.Protocol.Buffers;
using JT809.Protocol.Extensions;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;


namespace JT809.Protocol.MessagePack
{
    public ref struct JT809MessagePackReader
    {
        public ReadOnlySpan<byte> Reader { get; private set; }
        public ReadOnlySpan<byte> SrcBuffer { get; }
        public int ReaderCount { get; private set; }
        private ushort _calculateCheckCRCCode;
        private ushort _realCheckCRCCode;
        private bool _checkCRCCodeVali;
        /// <summary>
        /// 是否进行解码操作
        /// 若进行解码操作，则对应的是一个正常的包
        /// 若不进行解码操作，则对应的是一个非正常的包（头部包，数据体包等等）
        /// 主要用来一次性读取所有数据体内容操作
        /// </summary>
        private bool _decoded;
        private static byte[] decode5a01 = new byte[] { 0x5a, 0x01 };
        private static byte[] decode5a02 = new byte[] { 0x5a, 0x02 };
        private static byte[] decode5e01 = new byte[] { 0x5e, 0x01 };
        private static byte[] decode5e02 = new byte[] { 0x5e, 0x02 };
        /// <summary>
        /// 解码（转义还原）,计算校验和
        /// </summary>
        /// <param name="buffer"></param>
        public JT809MessagePackReader(ReadOnlySpan<byte> srcBuffer)
        {
            SrcBuffer = srcBuffer;
            ReaderCount = 0;
            _realCheckCRCCode = 0x00;
            _calculateCheckCRCCode = 0xFFFF;
            _checkCRCCodeVali = false;
            _decoded = false;
            Reader = srcBuffer;
        }
        /// <summary>
        /// 在解码的时候把校验和也计算出来，避免在循环一次进行校验
        /// </summary>
        /// <returns></returns>
        public void Decode()
         {
            Span<byte> span = new byte[SrcBuffer.Length];
            Decode(span);
            _decoded = true;
        }
        /// <summary>
        /// 在解码的时候把校验和也计算出来，避免在循环一次进行校验
        /// </summary>
        /// <returns></returns>
        public void Decode(Span<byte> allocateBuffer)
        {
            int offset = 0;
            int len = SrcBuffer.Length;
            allocateBuffer[offset++] = SrcBuffer[0];
            // 取出校验码看是否需要转义
            ReadOnlySpan<byte> checkCodeBufferSpan1 = SrcBuffer.Slice(len - 3, 2);
            int checkCodeLen = 0;
            if (TryDecode(checkCodeBufferSpan1, out byte value1))
            {
                //最后两位是转义的
                byte[] tmpCrc2 = new byte[2];
                checkCodeLen += 2;
                tmpCrc2[1] = value1;
                //从最后往前在取两位进行转义
                ReadOnlySpan<byte> checkCodeBufferSpan2 = SrcBuffer.Slice(len - 5, 2);
                if (TryDecode(checkCodeBufferSpan2, out byte value2))
                {
                    //转义成功
                    tmpCrc2[0] = value2;
                    checkCodeLen += 2;
                }
                else
                {
                    //转义不成功取当前最后一位
                    tmpCrc2[0] = checkCodeBufferSpan2[1];
                    checkCodeLen += 1;
                }
                _realCheckCRCCode = ReadUInt16(tmpCrc2);
            }
            else
            {   
                //最后两位不是转义的
                _realCheckCRCCode=ReadUInt16(checkCodeBufferSpan1);
                checkCodeLen += 2;
            }
            //转义数据长度
            len = len - checkCodeLen - 1 - 1;
            ReadOnlySpan<byte> tmpBufferSpan = SrcBuffer.Slice(1, len);
            for (int i = 0; i < tmpBufferSpan.Length; i++)
            {
                byte tmp = 0;
                if ((tmpBufferSpan.Length - i) >= 2)
                {
                    if (TryDecode(tmpBufferSpan.Slice(i, 2), out tmp))
                    {
                        i++;
                    }
                }
                else
                {
                    tmp = tmpBufferSpan[i];
                }
                allocateBuffer[offset++] = tmp;
                _calculateCheckCRCCode = (ushort)((_calculateCheckCRCCode << 8) ^ (ushort)CRCUtil.CRC[(_calculateCheckCRCCode >> 8) ^ tmp]);
            }
            allocateBuffer[offset++] = (byte)(_calculateCheckCRCCode >> 8);
            allocateBuffer[offset++] = (byte)_calculateCheckCRCCode;
            allocateBuffer[offset++] = SrcBuffer[SrcBuffer.Length- 1];
            _checkCRCCodeVali = (_calculateCheckCRCCode == _realCheckCRCCode);
            Reader = allocateBuffer.Slice(0, offset);
            _decoded = true;
        }

        public void FullDecode()
        {
            int offset = 0;
            Span<byte> span = new byte[SrcBuffer.Length];
            int len = SrcBuffer.Length;
            for (int i = 0; i < len; i++)
            {
                byte tmp = 0;
                if ((SrcBuffer.Length - i) >= 2)
                {
                    if (TryDecode(SrcBuffer.Slice(i, 2), out tmp))
                    {
                        i++;
                    }
                }
                else
                {
                    tmp = SrcBuffer[i];
                }
                span[offset++] = tmp;
            }
            Reader = span.Slice(0, offset);
        }

        private bool TryDecode(ReadOnlySpan<byte> buffer,out byte value)
        {
            if (buffer.SequenceEqual(decode5a01))
            {
                value = 0x5b;
                return true;
            }
            else if (buffer.SequenceEqual(decode5a02))
            {
                value = 0x5a;
                return true;
            }
            else if (buffer.SequenceEqual(decode5e01))
            {
                value = 0x5d;
                return true;
            }
            else if (buffer.SequenceEqual(decode5e02))
            {
                value = 0x5e;
                return true;
            }
            else
            {
                value = buffer[0];
                return false;
            }
        }
        public ushort CalculateCheckXorCode => _calculateCheckCRCCode;
        public ushort RealCheckXorCode => _realCheckCRCCode;
        public bool CheckXorCodeVali => _checkCRCCodeVali;
        public byte ReadStart()=> ReadByte();
        public byte ReadEnd()=> ReadByte();
        public ushort ReadUInt16()
        {
            var readOnlySpan = GetReadOnlySpan(2);
            ushort value = (ushort)((readOnlySpan[0] << 8) | (readOnlySpan[1]));
            return value;
        }
        public ushort ReadUInt16(ReadOnlySpan<byte> buffer)
        {
            return (ushort)((buffer[0] << 8) | (buffer[1])); 
        }
        public uint ReadUInt32()
        {
            var readOnlySpan = GetReadOnlySpan(4);
            uint value = (uint)((readOnlySpan[0] << 24) | (readOnlySpan[1] << 16) | (readOnlySpan[2] << 8) | readOnlySpan[3]);
            return value;
        }
        public int ReadInt32()
        {
            var readOnlySpan = GetReadOnlySpan(4);
            int value = (int)((readOnlySpan[0] << 24) | (readOnlySpan[1] << 16) | (readOnlySpan[2] << 8) | readOnlySpan[3]);
            return value;
        }
        public ulong ReadUInt64()
        {
            var readOnlySpan = GetReadOnlySpan(8);
            ulong value = (ulong)(
                (readOnlySpan[0] << 56) |
                (readOnlySpan[1] << 48) |
                (readOnlySpan[2] << 40) |
                (readOnlySpan[3] << 32) |
                (readOnlySpan[4] << 24) |
                (readOnlySpan[5] << 16) |
                (readOnlySpan[6] << 8) |
                 readOnlySpan[7]);
            return value;
        }
        public byte ReadByte()
        {
            var readOnlySpan = GetReadOnlySpan(1);
            return readOnlySpan[0];
        }
        public byte ReadVirtualByte()
        {
            var readOnlySpan = GetVirtualReadOnlySpan(1);
            return readOnlySpan[0];
        }
        public ushort ReadVirtualUInt16()
        {
            var readOnlySpan = GetVirtualReadOnlySpan(2);
            return (ushort)((readOnlySpan[0] << 8) | (readOnlySpan[1]));
        }
        public uint ReadVirtualUInt32()
        {
            var readOnlySpan = GetVirtualReadOnlySpan(4);
            return (uint)((readOnlySpan[0] << 24) | (readOnlySpan[1] << 16) | (readOnlySpan[2] << 8) | readOnlySpan[3]);
        }
        public ulong ReadVirtualUInt64()
        {
            var readOnlySpan = GetVirtualReadOnlySpan(8);
            return (ulong)(
                (readOnlySpan[0] << 56) |
                (readOnlySpan[1] << 48) |
                (readOnlySpan[2] << 40) |
                (readOnlySpan[3] << 32) |
                (readOnlySpan[4] << 24) |
                (readOnlySpan[5] << 16) |
                (readOnlySpan[6] << 8) |
                 readOnlySpan[7]);
        }
        /// <summary>
        /// 数字编码 大端模式、高位在前
        /// </summary>
        /// <param name="len"></param>
        public string ReadBigNumber(int len)
        {
            ulong result = 0;
            var readOnlySpan = GetReadOnlySpan(len);
            for (int i = 0; i < len; i++)
            {
                ulong currentData = (ulong)readOnlySpan[i] << (8 * (len - i - 1));
                result += currentData;
            }
            return result.ToString();
        }
        public ReadOnlySpan<byte> ReadArray(int len)
        {
            var readOnlySpan = GetReadOnlySpan(len);
            return readOnlySpan.Slice(0, len);
        }
        public ReadOnlySpan<byte> ReadArray(int start,int end)
        {
            return Reader.Slice(start,end);
        }
        public string ReadString(int len)
        {
            var readOnlySpan = GetReadOnlySpan(len);
            string value = JT809Constants.Encoding.GetString(readOnlySpan.Slice(0, len).ToArray());
            return value.Trim('\0');
        }
        public string ReadRemainStringContent()
        {
            var readOnlySpan = ReadContent(0);
            string value = JT809Constants.Encoding.GetString(readOnlySpan.ToArray());
            return value.Trim('\0');
        }
        public string ReadHex(int len)
        {
            var readOnlySpan = GetReadOnlySpan(len);
            string hex = HexUtil.DoHexDump(readOnlySpan, 0, len);
            return hex;
        }
        /// <summary>
        /// yyMMddHHmmss
        /// </summary>
        /// <param name="fromBase">>D2： 10  X2：16</param>
        public DateTime ReadDateTime6(string format = "X2")
        {
            DateTime d;
            try
            {
                var readOnlySpan = GetReadOnlySpan(6);
                int year = Convert.ToInt32(readOnlySpan[0].ToString(format)) + JT809Constants.DateLimitYear;
                int month = Convert.ToInt32(readOnlySpan[1].ToString(format));
                int day = Convert.ToInt32(readOnlySpan[2].ToString(format));
                int hour = Convert.ToInt32(readOnlySpan[3].ToString(format));
                int minute = Convert.ToInt32(readOnlySpan[4].ToString(format));
                int second = Convert.ToInt32(readOnlySpan[5].ToString(format));
                d = new DateTime(year, month, day, hour, minute, second);
            }
            catch (Exception)
            {
                d = JT809Constants.UTCBaseTime;
            }
            return d;
        }
        /// <summary>
        /// HH-mm-ss-msms
        /// HH-mm-ss-fff
        /// </summary>
        /// <param name="format">D2： 10  X2：16</param>
        public DateTime ReadDateTime5(string format = "X2")
        {
            DateTime d;
            try
            {
                var readOnlySpan = GetReadOnlySpan(5);
                d = new DateTime(
                DateTime.Now.Year,
                DateTime.Now.Month,
                DateTime.Now.Day,
                Convert.ToInt32(readOnlySpan[0].ToString(format)),
                Convert.ToInt32(readOnlySpan[1].ToString(format)),
                Convert.ToInt32(readOnlySpan[2].ToString(format)),
                Convert.ToInt32(((readOnlySpan[3] << 8) + readOnlySpan[4])));
            }
            catch
            {
                d = JT809Constants.UTCBaseTime;
            }
            return d;
        }
        /// <summary>
        /// YYYYMMDD
        /// </summary>
        /// <param name="format">D2： 10  X2：16</param>
        public DateTime ReadDateTime4(string format = "X2")
        {
            DateTime d;
            try
            {
                var readOnlySpan = GetReadOnlySpan(4);
                d = new DateTime(
               (Convert.ToInt32(readOnlySpan[0].ToString(format)) << 8) + Convert.ToByte(readOnlySpan[1]),
                Convert.ToInt32(readOnlySpan[2].ToString(format)),
                Convert.ToInt32(readOnlySpan[3].ToString(format)));
            }
            catch (Exception)
            {
                d = JT809Constants.UTCBaseTime;
            }
            return d;   
        }
        public DateTime ReadUTCDateTime()
        {
            DateTime d;
            try
            {
                ulong result = 0;
                var readOnlySpan = GetReadOnlySpan(8);
                for (int i = 0; i < 8; i++)
                {
                    ulong currentData = (ulong)readOnlySpan[i] << (8 * (8 - i - 1));
                    result += currentData;
                }
                d = JT809Constants.UTCBaseTime.AddSeconds(result).AddHours(8);
            }
            catch (Exception)
            {
                d = JT809Constants.UTCBaseTime;
            }
            return d;
        }
        public string ReadBCD(int len)
        {
            int count = len / 2;
            var readOnlySpan = GetReadOnlySpan(count);
            StringBuilder bcdSb = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                bcdSb.Append(readOnlySpan[i].ToString("X2"));
            }
            // todo:对于协议来说这个0是有意义的，下个版本在去掉
            return bcdSb.ToString().TrimStart('0');
        }
        private ReadOnlySpan<byte> GetReadOnlySpan(int count)
        {
            ReaderCount += count;
            return Reader.Slice(ReaderCount - count);
        }
        public ReadOnlySpan<byte> GetVirtualReadOnlySpan(int count)
        {
            return Reader.Slice(ReaderCount, count);
        }
        public ReadOnlySpan<byte> ReadContent(int count=0)
        {
            if (_decoded)
            {
                //内容长度=总长度-读取的长度-3（校验码1位+终止符1位）
                int totalContent = Reader.Length - ReaderCount - 3;
                //实际读取内容长度
                int realContent = totalContent - count;
                int tempReaderCount = ReaderCount;
                ReaderCount += realContent;
                return Reader.Slice(tempReaderCount, realContent);
            }
            else
            {
                return Reader.Slice(ReaderCount);
            }
        }
        public int ReadCurrentRemainContentLength()
        {
            if (_decoded)
            {
                //内容长度=总长度-读取的长度-3（校验码2位+终止符1位）
                return Reader.Length - ReaderCount - 3; 
            }
            else
            {
                return Reader.Length - ReaderCount;
            }
        }
        public void Skip(int count=1)
        {
            ReaderCount += count;
        }
    }
}
