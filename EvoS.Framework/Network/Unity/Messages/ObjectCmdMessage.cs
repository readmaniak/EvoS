namespace EvoS.Framework.Network.Unity.Messages
{
    [UNetMessage(serverMsgIds: new short[] {5}, clientMsgIds:new short[]{5})]
    public class ObjectCmdMessage : MessageBase
    {
        public int Hash;
        public NetworkInstanceId NetId;
        public byte[] Payload { get; set; }

        public override void Deserialize(NetworkReader reader)
        {
            Hash = (int) reader.ReadPackedUInt32();
            NetId = reader.ReadNetworkId();
            Payload = reader.ReadBytes((int) (reader.Length - reader.Position));
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.WritePackedUInt32((uint) Hash);
            writer.Write(NetId);
            writer.WriteBytesFull(Payload);
        }
    }
}