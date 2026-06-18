using System;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Snapshot réseau complet d'un joueur à un instant donné.
    ///
    /// C'est LE contrat de synchro entre owner et remote. Le owner remplit ce state à partir
    /// de sa simulation physique locale ; le remote le reçoit et interpole / rejoue l'IK.
    /// On ne synchronise jamais les forces ou les joints : uniquement le résultat (ce struct).
    ///
    /// Sérialisé en binaire compact pour envoi via Steamworks (étape 2). Layout fixe.
    /// </summary>
    [Serializable]
    public struct PlayerState
    {
        public uint Tick;             // numéro de simulation, pour l'ordre + buffer d'interpolation
        public Vector3 Position;      // position monde du Rigidbody
        public float Yaw;             // rotation horizontale du corps (degrés)
        public float Pitch;           // rotation verticale de la tête/caméra (degrés), utile à l'IK distante
        public Vector3 Velocity;      // vélocité linéaire, pour l'extrapolation côté remote
        public Vector3 LeftHandTarget;  // cible monde de la main gauche (IK)
        public Vector3 RightHandTarget; // cible monde de la main droite (IK)

        /// <summary>Taille fixe en octets d'un state sérialisé.</summary>
        public const int Size = sizeof(uint)        // Tick
                              + sizeof(float) * 3    // Position
                              + sizeof(float)        // Yaw
                              + sizeof(float)        // Pitch
                              + sizeof(float) * 3    // Velocity
                              + sizeof(float) * 3    // LeftHandTarget
                              + sizeof(float) * 3;   // RightHandTarget

        public byte[] Serialize()
        {
            var buffer = new byte[Size];
            WriteTo(buffer, 0);
            return buffer;
        }

        /// <summary>Écrit le state dans un buffer existant (évite l'alloc côté envoi).</summary>
        public int WriteTo(byte[] buffer, int offset)
        {
            int o = offset;
            o = Write(buffer, o, Tick);
            o = Write(buffer, o, Position.x); o = Write(buffer, o, Position.y); o = Write(buffer, o, Position.z);
            o = Write(buffer, o, Yaw);
            o = Write(buffer, o, Pitch);
            o = Write(buffer, o, Velocity.x); o = Write(buffer, o, Velocity.y); o = Write(buffer, o, Velocity.z);
            o = Write(buffer, o, LeftHandTarget.x); o = Write(buffer, o, LeftHandTarget.y); o = Write(buffer, o, LeftHandTarget.z);
            o = Write(buffer, o, RightHandTarget.x); o = Write(buffer, o, RightHandTarget.y); o = Write(buffer, o, RightHandTarget.z);
            return o;
        }

        public static PlayerState Deserialize(byte[] buffer, int offset = 0)
        {
            int o = offset;
            var s = new PlayerState();
            s.Tick = BitConverter.ToUInt32(buffer, o); o += sizeof(uint);
            s.Position = ReadVector3(buffer, ref o);
            s.Yaw = BitConverter.ToSingle(buffer, o); o += sizeof(float);
            s.Pitch = BitConverter.ToSingle(buffer, o); o += sizeof(float);
            s.Velocity = ReadVector3(buffer, ref o);
            s.LeftHandTarget = ReadVector3(buffer, ref o);
            s.RightHandTarget = ReadVector3(buffer, ref o);
            return s;
        }

        private static int Write(byte[] buffer, int offset, uint value)
        {
            BitConverter.TryWriteBytes(new Span<byte>(buffer, offset, sizeof(uint)), value);
            return offset + sizeof(uint);
        }

        private static int Write(byte[] buffer, int offset, float value)
        {
            BitConverter.TryWriteBytes(new Span<byte>(buffer, offset, sizeof(float)), value);
            return offset + sizeof(float);
        }

        private static Vector3 ReadVector3(byte[] buffer, ref int offset)
        {
            float x = BitConverter.ToSingle(buffer, offset); offset += sizeof(float);
            float y = BitConverter.ToSingle(buffer, offset); offset += sizeof(float);
            float z = BitConverter.ToSingle(buffer, offset); offset += sizeof(float);
            return new Vector3(x, y, z);
        }
    }
}
