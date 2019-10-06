using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EvoS.Framework.Assets.Serialized;
using EvoS.Framework.Assets.Serialized.Behaviours;
using EvoS.Framework.Logging;
using EvoS.Framework.Network;

namespace EvoS.Framework.Assets
{
    public class AssetFile : IDisposable
    {
        private StreamReader _stream;
        public SerializedFileHeader Header { get; set; }
        public SerializedFileMetadata Metadata { get; set; }
        private List<AssetFile> _fileMap = new List<AssetFile>();

        private static Dictionary<int, Type> _unityTypeMap = new Dictionary<int, Type>
        {
            {1, typeof(SerializedGameObject)},
            {2, typeof(SerializedComponent)},
            {114, typeof(SerializedMonoBehaviour)},
            {115, typeof(SerializedMonoScript)},
        };
        private static Dictionary<string, Type> _scriptTypeMap = new Dictionary<string, Type>
        {
        };

        static AssetFile()
        {
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                .Concat(Assembly.GetEntryAssembly().GetTypes()))
            {
                var attribute = type.GetCustomAttribute<SerializedMonoBehaviourAttribute>();
                if (attribute == null)
                    continue;

                _scriptTypeMap.Add(attribute.ClassName, type);
            }
        }

        public AssetFile(string filePath)
        {
            _fileMap.Add(this);

            _stream = new StreamReader(filePath);
            Header = new SerializedFileHeader(_stream);
            Metadata = new SerializedFileMetadata(_stream);

            LoadExternalReferences(filePath);
        }

        public IEnumerable<ObjectInfoEntry> GetObjectInfosByType(TypeEntry typeEntry)
        {
            foreach (var objInfo in Metadata.ObjectInfoTable.Entries)
            {
                if (objInfo.TypeId == typeEntry.Index)
                {
                    yield return objInfo;
                }
            }
        }

        private void LoadExternalReferences(string filePath)
        {
            foreach (var externalReference in Metadata.ExternalReferencesTable)
            {
                var fileName = externalReference.FileName;
                if (fileName.StartsWith("library/"))
                {
                    fileName = "resources/" + fileName.Substring(8);
                }

                var path = Path.Join(Path.GetDirectoryName(filePath), fileName);

                _fileMap.Add(new AssetFile(path));
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
        }

        public TypeEntry FindTypeById(int typeId)
        {
            foreach (var typeEntry in Metadata.TypeTree.BaseClasses)
            {
                if (typeEntry.TypeId == typeId)
                {
                    return typeEntry;
                }
            }

            return null;
        }
        
        public SerializedMonoChildBase ReadMonoScriptChild(SerializedMonoScript script)
        {
            if (!_scriptTypeMap.ContainsKey(script.ClassName))
            {
                return null;
            }

            var child = (SerializedMonoChildBase) Activator.CreateInstance(_scriptTypeMap[script.ClassName]);
            child.DeserializeAsset(this, _stream);

            return child;
        }

        public ISerializedItem ReadObject(SerializedComponent objInfo)
        {
            return ReadObject(objInfo.PathId, objInfo.FileId);
        }

        public ISerializedItem ReadObject(ObjectInfoEntry objInfo, int fileId = 0)
        {
            return ReadObject(objInfo.PathId, fileId);
        }

        public ISerializedItem ReadObject(long pathId, int fileId = 0, bool restorePos = true)
        {
            if (pathId == 0)
            {
                if (fileId != 0)
                {
                    throw new ArgumentException($"pathId was 0, but fileId was {fileId}");
                }

                return null;
            }

            if (fileId != 0)
            {
                return _fileMap[fileId].ReadObject(pathId, 0, restorePos);
            }

            var savedPos = _stream.Position;

            var objInfo = Metadata.ObjectInfoTable[pathId];

            var objType = Metadata.TypeTree.BaseClasses[objInfo.TypeId];

            _stream.Position = Header.DataOffset + objInfo.ByteStart;
            var endPos = Header.DataOffset + objInfo.ByteStart + objInfo.ByteSize;
            if (_unityTypeMap.ContainsKey(objType.TypeId))
            {
                var obj = (ISerializedItem) Activator.CreateInstance(_unityTypeMap[objType.TypeId]);
                obj.DeserializeAsset(this, _stream);

                var currentPos = _stream.Position;
                if (currentPos < endPos)
                {
                    if (obj is SerializedMonoBehaviour smb)
                    {
                        Log.Print(LogType.Warning,
                            $"Didn't fully read MB {smb.Script.ClassName}, {currentPos}/{endPos}");
                    }
                    else
                    {
                        Log.Print(LogType.Warning, $"Didn't fully read {obj.GetType().Name}, {currentPos}/{endPos}");
                    }
                }
                else if (currentPos > endPos)
                {
                    throw new IndexOutOfRangeException(
                        $"Read past the end of {obj.GetType().Name}, {currentPos}/{endPos}");
                }

                _stream.Position = savedPos;
                return obj;
            }
            else
            {
//                Log.Print(LogType.Warning, $"No type mapping for {objType} {objInfo}");
            }

            if (restorePos)
            {
                _stream.Position = savedPos;
            }

            return null;
        }
    }
}
