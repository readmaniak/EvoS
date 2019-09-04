﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EvoS.Framework.Network.NetworkMessages;
using EvoS.Framework.Network.Static;
using EvoS.Framework.Constants.Enums;
using EvoS.Framework.Logging;
using System.Reflection;

namespace EvoS.Framework.Network
{
    public class EvosMessageStream
    {
        private MemoryStream stream;
        private MemoryStream outputStream;
        private static Dictionary<Type, int> idsByType = new Dictionary<Type, int>();
        private static Dictionary<int, Type> typesByIds = new Dictionary<int, Type>();
        private static bool typesAndIdsLoaded = false;

        public EvosMessageStream(MemoryStream ms)
        {
            stream = ms;
            stream.Seek(0, SeekOrigin.Begin);
            outputStream = new MemoryStream();
        }

        private static Type GetTypeFromId(int id_)
        {
            if (!typesAndIdsLoaded)
                LoadTypesAndIds();

            Type ret;
            if (typesByIds.TryGetValue(id_, out ret))
                return ret;
            else
                throw new EvosMessageStreamException($"Can't find a registered type from id {id_}");
        }

        private static int GetIdFromType(Type T)
        {
            if (!typesAndIdsLoaded)
                LoadTypesAndIds();

            int ret;
            if (idsByType.TryGetValue(T, out ret))
                return ret;
            else
                throw new EvosMessageStreamException($"Can't find a registered id from type {T.Name}");
        }

        public MemoryStream GetOutputStream() => outputStream;

        public bool ReadBool() => stream.ReadByte() != 0;

        public long ReadLong()
        {
            long n = (long)ReadVarInt();
            return (long)(n >> 1 ^ -(n & 1L));
        }

        public unsafe double ReadDouble()
        {
            long num = (long)ReadVarInt();
            double value = *(double*)(&num);
            return value;
        }

        public unsafe int WriteDouble(double value)
        {
            /*u*/
            long value2 = /*(ulong)*/(*(long*)(&value));
            return WriteVarInt(value2);
        }

        public unsafe float ReadFloat()
        {
            /*u*/
            int num = ReadVarInt();
            float value = *(float*)(&num);
            return value;
        }

        public unsafe int WriteFloat(float value)
        {
            /*u*/
            int value2 = (*(/*u*/int*)(&value));
            return WriteVarInt(value2);
        }

        public int ReadVarInt()
        {
            int shift = 0;
            int result = 0;
            int byteValue;

            while (true)
            {
                byteValue = stream.ReadByte();
                //Console.WriteLine("read value: " + byteValue);
                if (byteValue == -1) break;
                result |= ((byteValue & 0x7f) << shift);
                shift += 7;
                if ((byteValue & 0x80) != 0x80) break;
            }

            return result;
        }

        public String ReadString()
        {
            int data_length = ReadVarInt();

            byte[] buffer;

            if (data_length == 0)
                return null;
            else if (data_length == 1)
                return string.Empty;
            else
            {
                int string_length = ReadVarInt();
                buffer = new byte[string_length];
                stream.Read(buffer, 0, string_length);
                return Encoding.UTF8.GetString(buffer, 0, buffer.Length);
            }
        }

        /*
         * Writes a number an returns the amount of bytes written
         */
        public int WriteVarInt(int value)
        {
            int byteValue;
            int byteCount = 0;

            while (true)
            {
                byteValue = value & 0x7f;
                value >>= 7;

                if (value != 0)
                    byteValue |= 0x80;

                //Console.WriteLine("writing byte: " + byteValue);
                outputStream.WriteByte((byte)byteValue);
                byteCount++;

                if (value == 0) return byteCount;
            }
        }

        public int WriteVarInt(long value)
        {
            int byteValue;
            int byteCount = 0;

            while (true)
            {
                byteValue = (int)(value & 0x7f);
                value >>= 7;

                if (value != 0)
                    byteValue |= 0x80;

                //Console.WriteLine("writing byte: " + byteValue);
                outputStream.WriteByte((byte)byteValue);
                byteCount++;

                if (value == 0) return byteCount;
            }
        }

        public int WriteString(String str)
        {
            int byteCount = 0;

            if (str == null)
                return WriteVarInt(0);
            if (str.Length == 0)
                return WriteVarInt(1);

            byteCount += WriteVarInt(str.Length + 1);
            byteCount += WriteVarInt(str.Length);
            byte[] buffer = Encoding.GetEncoding("UTF-8").GetBytes(str.ToCharArray());
            outputStream.Write(buffer, 0, buffer.Length);
            byteCount += buffer.Length;

            return byteCount;
        }

        public int WriteLong(long value)
        {
            long a = value << 1 ^ value >> 63;
            return WriteVarInt(a);
        }

        public int WriteBool(bool value)
        {
            outputStream.WriteByte(value ? ((byte)1) : ((byte)0));
            return 1;
        }

        public int WriteDateTime(DateTime value)
        {
            long d = value.ToBinary();
            return WriteLong(d);
        }

        public DateTime ReadDateTime()
        {
            DateTime d = DateTime.FromBinary(ReadLong());
            return d;
        }

        public object ReadGeneral()
        {
            /*
             * TODO: think a better name
             * Note to self: properties and fields are read by declaration order, so i should sort them by name on declaration
             */
            int type_id = ReadVarInt();

            if (type_id == 0)
            {
                Console.WriteLine("NULL!");
                return null;
            }
            else if (type_id == 1)
                return new object();
            else
            {
                Type T = GetTypeFromId(type_id);

                Console.WriteLine("Deserializing " + T.Name);
                object obj = T.GetConstructor(Type.EmptyTypes).Invoke(new object[] { });
                //BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
                MemberInfo[] members = T.GetMembers();

                // TODO?: the two ifs are repeated, maybe there is a way to merge them to make the code more readable
                for (int i = 0; i < members.Length; i++)
                {
                    MemberInfo member = members[i];

                    if (member.MemberType == MemberTypes.Field && !(((FieldInfo)member).IsNotSerialized))
                    {
                        FieldInfo field = (FieldInfo)member;

                        if (field.FieldType == typeof(String))
                        {
                            String value = ReadString();
                            Console.WriteLine($"{T.Name}.{member.Name} ({field.FieldType}) = {value}");
                            T.GetField(field.Name).SetValue(obj, value);
                        }
                        else if (field.FieldType == typeof(long))
                        {
                            long value = ReadLong();
                            Console.WriteLine($"{T.Name}.{member.Name} ({field.FieldType}) = {value}");
                            T.GetField(field.Name).SetValue(obj, value);
                        }
                        else if (field.FieldType == typeof(Int32) || field.FieldType == typeof(int))
                        {
                            int value = ReadVarInt();
                            Console.WriteLine($"{T.Name}.{member.Name} ({field.FieldType}) = {value}");
                            T.GetField(field.Name).SetValue(obj, value);
                        }
                        else if (field.FieldType.IsEnum)
                        {
                            int value = ReadVarInt();
                            Console.WriteLine($"{T.Name}.{member.Name} ({field.FieldType}) = {value}");
                            T.GetField(field.Name).SetValue(obj, value);
                        }
                        else if (field.FieldType == typeof(bool))
                        {
                            bool value = ReadBool();
                            Console.WriteLine($"{T.Name}.{member.Name} ({field.FieldType}) = {value}");
                            T.GetField(field.Name).SetValue(obj, value);
                        }
                        else if (field.FieldType == typeof(DateTime))
                        {
                            DateTime value = ReadDateTime();
                            Console.WriteLine($"{T.Name}.{member.Name} ({field.FieldType}) = {value}");
                            T.GetField(field.Name).SetValue(obj, value);
                        }
                        else
                        {
                            Console.WriteLine($"==== Woops! =====: I dont know how to read {field.GetType().Name}");
                            T.GetField(field.Name).SetValue(obj, ReadGeneral());
                        }
                    }
                    else if (member.MemberType == MemberTypes.Property)
                    {
                        PropertyInfo property = (PropertyInfo)member;

                        if (property.PropertyType == typeof(String))
                        {
                            String value = ReadString();
                            Console.WriteLine($"{T.Name}.{member.Name} ({property.PropertyType}) = {value}");
                            T.GetProperty(property.Name).SetValue(obj, value);
                        }
                        else if (property.PropertyType == typeof(long))
                        {
                            long value = ReadLong();
                            Console.WriteLine($"{T.Name}.{member.Name} ({property.PropertyType}) = {value}");
                            T.GetProperty(property.Name).SetValue(obj, value);
                        }
                        else if (property.PropertyType == typeof(Int32) || property.PropertyType == typeof(int))
                        {
                            int value = ReadVarInt();
                            Console.WriteLine($"{T.Name}.{member.Name} ({property.PropertyType}) = {value}");
                            T.GetProperty(property.Name).SetValue(obj, value);
                        }
                        else if (property.PropertyType.IsEnum)
                        {
                            int value = ReadVarInt();
                            Console.WriteLine($"{T.Name}.{member.Name} ({property.PropertyType}) = {value}");
                            T.GetProperty(property.Name).SetValue(obj, value);
                        }
                        else if (property.PropertyType == typeof(bool))
                        {
                            bool value = ReadBool();
                            Console.WriteLine($"{T.Name}.{member.Name} ({property.PropertyType}) = {value}");
                            T.GetProperty(property.Name).SetValue(obj, value);
                        }
                        else if (property.PropertyType == typeof(DateTime))
                        {
                            DateTime value = ReadDateTime();
                            Console.WriteLine($"{T.Name}.{member.Name} ({property.PropertyType}) = {value}");
                            T.GetProperty(property.Name).SetValue(obj, value);
                        }

                        else
                        {
                            Console.WriteLine($"{T.Name}.{member.Name}");
                            Console.WriteLine($"==== Woops! =====: trying to read property of type {property.PropertyType.Name}");
                            T.GetProperty(property.Name).SetValue(obj, ReadGeneral());
                        }
                    }
                }

                return obj;
            }
        }

        public void WriteGeneral(object responseObject)
        {
            Type responseType;

            /*
             * TODO: See later:
             *      LobbyStatusNotification.ErrorReportRate (System.Nullable`1[System.TimeSpan]) =
             *      ==== Woops! =====: I dont know how to write Nullable`1
             */

            //int type_id = ReadVarInt();

            if (responseObject == null)
            {
                Log.Print(LogType.Error, "NULL!");
                WriteVarInt(0);
                return;
            }
            else if (responseObject.GetType() == typeof(object))
            {
                WriteVarInt(1);
                return;
            }
            else
            {
                responseType = responseObject.GetType();
                Log.Print(LogType.Debug, "Serializing " + responseType.Name);

                try
                {
                    //If is a registered type (not a primitive), write type id
                    int type_id = GetIdFromType(responseType);
                    WriteVarInt(type_id);
                }
                catch { }

                //BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
                MemberInfo[] members = responseType.GetMembers();

                for (int i = 0; i < members.Length; i++)
                {
                    MemberInfo member = members[i];
                    Type fieldPropertyType = null;
                    object value;

                    if (member.MemberType == MemberTypes.Field && !(((FieldInfo)member).IsNotSerialized))
                    {
                        FieldInfo field = (FieldInfo)member;
                        fieldPropertyType = field.FieldType;
                        if (field.Attributes.HasFlag(FieldAttributes.InitOnly))
                            continue;
                        value = responseType.GetField(field.Name).GetValue(responseObject);
                    }
                    else if (member.MemberType == MemberTypes.Property)
                    {
                        PropertyInfo property = (PropertyInfo)member;
                        fieldPropertyType = property.PropertyType;
                        value = responseType.GetProperty(property.Name).GetValue(responseObject);
                    }
                    else
                        continue; // Not a property and not a field, should do nothing

                    // Write based on data type

                    Log.Print(LogType.Debug, $"{responseType.Name}.{member.Name} ({fieldPropertyType}) = {value}");
                    if (fieldPropertyType == typeof(String))
                        WriteString((String)value);

                    else if (fieldPropertyType == typeof(long) || fieldPropertyType == typeof(Int64))
                        WriteLong((long)value);

                    else if (fieldPropertyType == typeof(Int32) || fieldPropertyType == typeof(int))
                        WriteVarInt((int)value);

                    else if (fieldPropertyType.IsEnum)
                        WriteVarInt((int)value);

                    else if (fieldPropertyType == typeof(bool))
                        WriteBool((bool)value);

                    else if (fieldPropertyType == typeof(DateTime))
                        WriteDateTime((DateTime)value);

                    else if (fieldPropertyType == typeof(Double))
                        WriteDouble((Double)value);

                    else if (fieldPropertyType == typeof(float))
                        WriteFloat((float)value);

                    else
                    {
                        Log.Print(LogType.Debug, $"==== Woops! =====: I dont know how to write {fieldPropertyType.Name}");

                        WriteGeneral(value);
                    }

                }
            }
        }

        private static void LoadTypeAndId(int id_, Type T)
        {
            typesByIds.Add(id_, T);
            idsByType.Add(T, id_);
        }

        private static void LoadTypesAndIds()
        {
            typesAndIdsLoaded = true;

            /*LoadTypeAndId(757, typeof(JoinGameRequest);
            LoadTypeAndId(758, typeof(CreateGameResponse);
            LoadTypeAndId(759, typeof(CreateGameRequest);
            LoadTypeAndId(760, typeof(SyncNotification);
            LoadTypeAndId(761, typeof(SetRegionRequest);
            LoadTypeAndId(762, typeof(UnsubscribeFromCustomGamesRequest);
            LoadTypeAndId(763, typeof(SubscribeToCustomGamesRequest);
            LoadTypeAndId(764, typeof(LobbyCustomGamesNotification);
            LoadTypeAndId(765, typeof(List`1);
            LoadTypeAndId(766, typeof(LobbyGameInfo[]);
            LoadTypeAndId(767, typeof(LobbyGameplayOverridesNotification),// */
            LoadTypeAndId(768, typeof(LobbyStatusNotification));
            LoadTypeAndId(769, typeof(ServerMessageOverrides));
            LoadTypeAndId(770, typeof(ServerMessage));
            LoadTypeAndId(771, typeof(ServerLockState));
            LoadTypeAndId(772, typeof(ConnectionQueueInfo));
            /*LoadTypeAndId(773, typeof(LobbyServerReadyNotification));
            LoadTypeAndId(774, typeof(LobbyPlayerGroupInfo));
            LoadTypeAndId(775, typeof(EnvironmentType));
            LoadTypeAndId(776, typeof(List`1));
            LoadTypeAndId(777, typeof(PersistedCharacterData[])),// */
            LoadTypeAndId(778, typeof(RegisterGameClientResponse));
            LoadTypeAndId(779, typeof(LobbySessionInfo));
            LoadTypeAndId(780, typeof(ProcessType));
            LoadTypeAndId(781, typeof(AuthInfo));
            LoadTypeAndId(782, typeof(AuthType));
            LoadTypeAndId(783, typeof(RegisterGameClientRequest));
            LoadTypeAndId(784, typeof(LobbyGameClientSystemInfo));
            //LoadTypeAndId(785, typeof(AssignGameClientResponse));
            LoadTypeAndId(786, typeof(LobbyGameClientProxyInfo));
            //LoadTypeAndId(787, typeof(LobbyGameClientProxyStatus));
            //LoadTypeAndId(788, typeof(AssignGameClientRequest));
        }
    }
}
