﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TC = System.ComponentModel.TypeConverterAttribute;
using EXP = System.ComponentModel.ExpandableObjectConverter;
using SharpDX;
using System.Xml;
using System.Text.RegularExpressions;




/*

Parts of this are adapted from CamxxCore's RageAudioTool, although it's been completely reworked for CW.
-dexyfex


https://github.com/CamxxCore/RageAudioTool

MIT License

Copyright (c) 2017 Cameron Berry

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/



namespace CodeWalker.GameFiles
{
    public enum RelDatFileType : uint
    {
        Dat4 = 4,
        Dat10ModularSynth = 10,
        Dat15DynamicMixer = 15,
        Dat16Curves = 16,
        Dat22Categories = 22,
        Dat54DataEntries = 54,
        Dat149 = 149,
        Dat150 = 150,
        Dat151 = 151
    }

    [TC(typeof(EXP))] public class RelFile : GameFile, PackedFile
    {
        public byte[] RawFileData { get; set; }
        public RelDatFileType RelType { get; set; }
        public uint DataLength { get; set; }
        public byte[] DataBlock { get; set; }
        public uint DataUnkVal { get; set; }
        public uint NameTableLength { get; set; }
        public uint NameTableCount { get; set; }
        public uint[] NameTableOffsets { get; set; }
        public string[] NameTable { get; set; }
        public uint IndexCount { get; set; }
        public uint IndexStringFlags { get; set; }
        public RelIndexHash[] IndexHashes { get; set; }
        public RelIndexString[] IndexStrings { get; set; }
        public uint HashTableCount { get; set; }
        public uint[] HashTableOffsets { get; set; }
        public MetaHash[] HashTable { get; set; }
        public uint PackTableCount { get; set; }
        public uint[] PackTableOffsets { get; set; }
        public MetaHash[] PackTable { get; set; }

        public RelData[] RelDatas { get; set; }
        public RelData[] RelDatasSorted { get; set; }
        //testing zone for decoding .rel audio files.

        public Dictionary<uint, RelData> RelDataDict { get; set; } = new Dictionary<uint, RelData>();


        //fields used by the editor:
        public bool HasChanged { get; set; } = false;
        public List<string> SaveWarnings = null;


        public RelFile() : base(null, GameFileType.Rel)
        {
        }
        public RelFile(RpfFileEntry entry) : base(entry, GameFileType.Rel)
        {
            RpfFileEntry = entry;
        }

        public void Load(byte[] data, RpfFileEntry entry)
        {
            RawFileData = data;
            if (entry != null)
            {
                RpfFileEntry = entry;
                Name = entry.Name;
            }

            MemoryStream ms = new MemoryStream(data);
            BinaryReader br = new BinaryReader(ms);
            StringBuilder sb = new StringBuilder();

            RelType = (RelDatFileType)br.ReadUInt32(); //type

            DataLength = br.ReadUInt32(); //length of data block
            DataBlock = br.ReadBytes((int)DataLength); //main data block...

            NameTableLength = br.ReadUInt32(); //length of this nametable block
            NameTableCount = br.ReadUInt32();
            if (NameTableCount > 0)
            {
                uint[] ntoffsets = new uint[NameTableCount]; //string offsets
                for (uint i = 0; i < NameTableCount; i++)
                {
                    ntoffsets[i] = br.ReadUInt32();
                }
                NameTableOffsets = ntoffsets;
                string[] names = new string[NameTableCount];
                for (uint i = 0; i < NameTableCount; i++)
                {
                    sb.Clear();
                    while (true)
                    {
                        char c = (char)br.ReadByte();
                        if (c != 0) sb.Append(c);
                        else break;
                    }
                    names[i] = sb.ToString();

                    //JenkIndex.Ensure(names[i]); //really need both here..?
                    JenkIndex.Ensure(names[i].ToLowerInvariant());
                }
                NameTable = names;
            }

            IndexCount = br.ReadUInt32(); //count of index items
            if (IndexCount > 0)
            {
                if ((RelType == RelDatFileType.Dat4) && (NameTableLength == 4))//audioconfig.dat4.rel    //checking NameTableLength here doesn't make sense!
                {
                    IndexStringFlags = br.ReadUInt32(); //what is this?  2524
                    if (IndexStringFlags != 2524)
                    { }
                    RelIndexString[] indexstrs = new RelIndexString[IndexCount];
                    for (uint i = 0; i < IndexCount; i++)
                    {
                        byte sl = br.ReadByte();
                        sb.Clear();
                        for (int j = 0; j < sl; j++)
                        {
                            char c = (char)br.ReadByte();
                            if (c != 0) sb.Append(c);
                        }
                        RelIndexString ristr = new RelIndexString();
                        ristr.Name = sb.ToString();
                        ristr.Offset = br.ReadUInt32();
                        ristr.Length = br.ReadUInt32();
                        indexstrs[i] = ristr;
                    }
                    IndexStrings = indexstrs;
                }
                else //for all other .rel files...
                {
                    RelIndexHash[] indexhashes = new RelIndexHash[IndexCount];
                    for (uint i = 0; i < IndexCount; i++)
                    {
                        RelIndexHash rihash = new RelIndexHash();
                        rihash.Name = new MetaHash(br.ReadUInt32());
                        rihash.Offset = br.ReadUInt32();
                        rihash.Length = br.ReadUInt32();
                        indexhashes[i] = rihash;
                    }
                    IndexHashes = indexhashes;
                }
            }


            HashTableCount = br.ReadUInt32();
            if (HashTableCount != 0)
            {
                uint[] htoffsets = new uint[HashTableCount];
                MetaHash[] hthashes = new MetaHash[HashTableCount];
                for (uint i = 0; i < HashTableCount; i++)
                {
                    htoffsets[i] = br.ReadUInt32();

                    var pos = ms.Position;
                    ms.Position = htoffsets[i];
                    hthashes[i] = new MetaHash(br.ReadUInt32());
                    ms.Position = pos;
                }
                HashTableOffsets = htoffsets;
                HashTable = hthashes;
            }

            PackTableCount = br.ReadUInt32();
            if (PackTableCount != 0)
            {
                uint[] ptoffsets = new uint[PackTableCount];
                MetaHash[] pthashes = new MetaHash[PackTableCount];
                for (uint i = 0; i < PackTableCount; i++)
                {
                    ptoffsets[i] = br.ReadUInt32();

                    var pos = ms.Position;
                    ms.Position = ptoffsets[i];
                    pthashes[i] = new MetaHash(br.ReadUInt32());
                    ms.Position = pos;
                }
                PackTableOffsets = ptoffsets;
                PackTable = pthashes;
            }

            if (ms.Position != ms.Length)
            { }
            //EOF!

            br.Dispose();
            ms.Dispose();


            ParseDataBlock();

            //BuildHashMaps();


            Loaded = true;
        }


        private void ParseDataBlock()
        {



            MemoryStream ms = new MemoryStream(DataBlock);
            BinaryReader br = new BinaryReader(ms);

            DataUnkVal = br.ReadUInt32(); //3 bytes used... for? ..version? flags?
            #region DataUnkVal unk values test
            //switch (DataUnkVal)
            //{
            //    case 5252715: //dlcbusiness_amp.dat10.rel
            //    case 5301323: //dlcbeach_game.dat149.rel
            //    case 5378673: //dlcmpheist_game.dat150.rel
            //    case 5750395: //dlcbeach_game.dat150.rel
            //    case 6353778: //dlcbeach_game.dat151.rel
            //    case 6894089: //dlcpilotschool_game.dat151.rel
            //    case 6978435: //dlcxmas2_amp.dat10.rel
            //    case 7126027: //audioconfig.dat4.rel
            //    case 7314721: //dlcmpheist_amp.dat10.rel
            //    case 7516460: //dlcpd03_game.dat151.rel
            //    case 7917027: //dlcluxe_amp.dat10.rel
            //    case 7921508: //dlcluxe_game.dat151.rel
            //    case 8149475: //dlcluxe2_amp.dat10.rel
            //    case 8751734: //dlcsfx1_game.dat151.rel
            //    case 9028036: //dlchalloween_amp.dat10.rel
            //    case 9037528: //dlclowrider_amp.dat10.rel
            //    case 9458585: //dlcapartment_amp.dat10.rel
            //    case 9486222: //dlcapartment_mix.dat15.rel
            //    case 9806108: //mpvalentines2_amp.dat10.rel
            //    case 9813679: //dlcjanuary2016_amp.dat10.rel
            //    case 10269543://dlclow2_amp.dat10.rel
            //    case 10891463://dlcexec1_amp.dat10.rel
            //    case 11171338://dlcstunt_amp.dat10.rel
            //    case 11918985://dlcbiker_amp.dat10.rel
            //    case 12470522://dlcimportexport_amp.dat10.rel
            //    case 12974726://audioconfig.dat4.rel
            //    case 13117164://dlcspecialraces_amp.dat10.rel
            //        break;
            //    default:
            //        break;
            //}
            #endregion


            List<RelData> reldatas = new List<RelData>();
            if (IndexHashes != null)
            {
                foreach (var indexhash in IndexHashes)
                {
                    reldatas.Add(ReadRelData(br, indexhash));
                }
            }
            else if (IndexStrings != null)
            {
                foreach (var indexstr in IndexStrings)
                {
                    reldatas.Add(ReadRelData(br, indexstr));
                }
            }
            RelDatas = reldatas.ToArray();

            reldatas.Sort((d1, d2) => d1.DataOffset.CompareTo(d2.DataOffset));
            RelDatasSorted = reldatas.ToArray();


            br.Dispose();
            ms.Dispose();




            RelDataDict.Clear();
            foreach (var reldata in RelDatas)
            {
                if ((reldata.NameHash == 0) && !string.IsNullOrEmpty(reldata.Name))
                {
                    reldata.NameHash = JenkHash.GenHash(reldata.Name); //should this be lower case?
                    JenkIndex.Ensure(reldata.Name);
                    JenkIndex.Ensure(reldata.Name.ToLowerInvariant()); //which one to use?
                }

                //if (reldata.NameHash == 0)
                //{ }//no hits here
                //if (RelDataDict.ContainsKey(reldata.NameHash))
                //{ }//no hits here

                RelDataDict[reldata.NameHash] = reldata;
            }
            foreach (var reldata in RelDatas)
            {
                RelSound snd = reldata as RelSound;
                if (snd != null)
                {
                    if (snd.AudioTracksCount > 0)
                    {
                        snd.AudioTracks = new RelData[snd.AudioTracksCount];
                        for (int i = 0; i < snd.AudioTracksCount; i++)
                        {
                            var audhash = snd.AudioTrackHashes[i];
                            RelData auddata = null;
                            if (RelDataDict.TryGetValue(audhash, out auddata))
                            {
                                snd.AudioTracks[i] = auddata;
                            }
                            else
                            { }
                        }
                    }
                    if (snd.AudioContainers != null)
                    {
                        foreach (var cnt in snd.AudioContainers)
                        {
                            string cname = JenkIndex.TryGetString(cnt.Hash);
                            if (!string.IsNullOrEmpty(cname))
                            { }
                            else
                            { }
                        }
                    }
                }
            }

        }




        private RelData ReadRelData(BinaryReader br, RelIndexHash h)
        {
            return ReadRelData(br, null, h.Name, h.Offset, h.Length);
        }
        private RelData ReadRelData(BinaryReader br, RelIndexString s)
        {
            return ReadRelData(br, s.Name, JenkHash.GenHash(s.Name.ToLowerInvariant()), s.Offset, s.Length);
        }
        private RelData ReadRelData(BinaryReader br, string name, MetaHash hash, uint offset, uint length)
        {
            br.BaseStream.Position = offset;
            byte[] data = br.ReadBytes((int)length);


            RelData d = new RelData(this); //use this base object to construct the derived one...
            d.Name = name;
            d.NameHash = hash;
            d.DataOffset = offset;
            d.DataLength = length;
            d.Data = data;


            using (BinaryReader dbr = new BinaryReader(new MemoryStream(data)))
            {
                d.ReadType(dbr);

                switch (RelType)
                {
                    case RelDatFileType.Dat4:   //speech.dat4.rel, audioconfig.dat4.rel
                        return ReadData4(d, dbr);
                    case RelDatFileType.Dat10ModularSynth:  //amp.dat10.rel
                        return ReadData10(d, dbr);
                    case RelDatFileType.Dat15DynamicMixer:  //mix.dat15.rel
                        return ReadData15(d, dbr);
                    case RelDatFileType.Dat16Curves:  //curves.dat16.rel
                        return ReadData16(d, dbr);
                    case RelDatFileType.Dat22Categories:  //categories.dat22.rel
                        return ReadData22(d, dbr);
                    case RelDatFileType.Dat54DataEntries:  //sounds.dat54.rel
                        return ReadData54(d, dbr);
                    case RelDatFileType.Dat149: //game.dat149.rel
                        return ReadData149(d, dbr);
                    case RelDatFileType.Dat150: //game.dat150.rel
                        return ReadData150(d, dbr);
                    case RelDatFileType.Dat151: //game.dat151.rel
                        return ReadData151(d, dbr);
                    default:
                        return d; //shouldn't get here...
                }
            }
        }



        private RelData ReadData4(RelData d, BinaryReader br)
        {
            if (NameTableLength == 4) //(for audioconfig.dat4.rel)
            {
            }
            else //(for eg speech.dat4.rel)
            {
            }
            return d;
        }
        private RelData ReadData10(RelData d, BinaryReader br)
        {
            return d;
        }
        private RelData ReadData15(RelData d, BinaryReader br)
        {
            return d;
        }
        private RelData ReadData16(RelData d, BinaryReader br)
        {
            return d;
        }
        private RelData ReadData22(RelData d, BinaryReader br)
        {
            //RelSound s = new RelSound(d, br);
            //return s;
            return d;
        }
        private RelData ReadData54(RelData d, BinaryReader br)
        {
            switch ((Dat54SoundType)d.TypeID)
            {
                case Dat54SoundType.LoopingSound: return new Dat54LoopingSound(d, br);
                case Dat54SoundType.EnvelopeSound: return new Dat54EnvelopeSound(d, br);
                case Dat54SoundType.TwinLoopSound: return new Dat54TwinLoopSound(d, br);
                case Dat54SoundType.SpeechSound: return new Dat54SpeechSound(d, br);
                case Dat54SoundType.OnStopSound: return new Dat54OnStopSound(d, br);
                case Dat54SoundType.WrapperSound: return new Dat54WrapperSound(d, br);
                case Dat54SoundType.SequentialSound: return new Dat54SequentialSound(d, br);
                case Dat54SoundType.StreamingSound: return new Dat54StreamingSound(d, br);
                case Dat54SoundType.RetriggeredOverlappedSound: return new Dat54RetriggeredOverlappedSound(d, br);
                case Dat54SoundType.CrossfadeSound: return new Dat54CrossfadeSound(d, br);
                case Dat54SoundType.CollapsingStereoSound: return new Dat54CollapsingStereoSound(d, br);
                case Dat54SoundType.SimpleSound: return new Dat54SimpleSound(d, br);
                case Dat54SoundType.MultitrackSound: return new Dat54MultitrackSound(d, br);
                case Dat54SoundType.RandomizedSound: return new Dat54RandomizedSound(d, br);
                case Dat54SoundType.EnvironmentSound: return new Dat54EnvironmentSound(d, br);
                case Dat54SoundType.DynamicEntitySound: return new Dat54DynamicEntitySound(d, br);
                case Dat54SoundType.SequentialOverlapSound: return new Dat54SequentialOverlapSound(d, br);
                case Dat54SoundType.ModularSynthSound: return new Dat54ModularSynthSound(d, br);
                case Dat54SoundType.GranularSound: return new Dat54GranularSound(d, br);
                case Dat54SoundType.DirectionalSound: return new Dat54DirectionalSound(d, br);
                case Dat54SoundType.KineticSound: return new Dat54KineticSound(d, br);
                case Dat54SoundType.SwitchSound: return new Dat54SwitchSound(d, br);
                case Dat54SoundType.VariableCurveSound: return new Dat54VariableCurveSound(d, br);
                case Dat54SoundType.VariablePrintValueSound: return new Dat54VariablePrintValueSound(d, br);
                case Dat54SoundType.VariableBlockSound: return new Dat54VariableBlockSound(d, br);
                case Dat54SoundType.IfSound: return new Dat54IfSound(d, br);
                case Dat54SoundType.MathOperationSound: return new Dat54MathOperationSound(d, br);
                case Dat54SoundType.ParameterTransformSound: return new Dat54ParameterTransformSound(d, br);
                case Dat54SoundType.FluctuatorSound: return new Dat54FluctuatorSound(d, br);
                case Dat54SoundType.AutomationSound: return new Dat54AutomationSound(d, br);
                case Dat54SoundType.ExternalStreamSound: return new Dat54ExternalStreamSound(d, br);
                case Dat54SoundType.SoundSet: return new Dat54SoundSet(d, br);
                case Dat54SoundType.Unknown: return new Dat54UnknownSound(d, br);
                case Dat54SoundType.Unknown2: return new Dat54UnknownSound2(d, br);
                case Dat54SoundType.SoundList: return new Dat54SoundList(d, br);
                default:
                    return new Dat54Sound(d, br); //shouldn't get here
            }
        }
        private RelData ReadData149(RelData d, BinaryReader br)
        {
            return ReadData151(d, br);//same as 151?
        }
        private RelData ReadData150(RelData d, BinaryReader br)
        {
            return ReadData151(d, br);//same as 151?
        }
        private RelData ReadData151(RelData d, BinaryReader br)
        {
            switch ((Dat151RelType)d.TypeID)
            {
                case Dat151RelType.AmbientEmitterList: return new Dat151AmbientEmitterList(d, br);
                case Dat151RelType.AmbientZone: return new Dat151AmbientZone(d, br);
                case Dat151RelType.AmbientEmitter: return new Dat151AmbientEmitter(d, br);
                case Dat151RelType.AmbientZoneList: return new Dat151AmbientZoneList(d, br);
                case Dat151RelType.Collision: return new Dat151Collision(d, br); //maybe for vehicle
                case Dat151RelType.WeaponAudioItem: return new Dat151WeaponAudioItem(d, br);
                case Dat151RelType.StartTrackAction: return new Dat151StartTrackAction(d, br);
                case Dat151RelType.StopTrackAction: return new Dat151StopTrackAction(d, br);
                case Dat151RelType.Mood: return new Dat151Mood(d, br);
                case Dat151RelType.SetMoodAction: return new Dat151SetMoodAction(d, br);
                case Dat151RelType.PlayerAction: return new Dat151PlayerAction(d, br);
                case Dat151RelType.StartOneShotAction: return new Dat151StartOneShotAction(d, br);
                case Dat151RelType.StopOneShotAction: return new Dat151StopOneShotAction(d, br);
                case Dat151RelType.FadeOutRadioAction: return new Dat151FadeOutRadioAction(d, br);
                case Dat151RelType.FadeInRadioAction: return new Dat151FadeInRadioAction(d, br);
                case Dat151RelType.Mod: return new Dat151Mod(d, br);
                case Dat151RelType.Interior: return new Dat151Interior(d, br);
                case Dat151RelType.InteriorRoom: return new Dat151InteriorRoom(d, br);
                case Dat151RelType.Unk117: return new Dat151Unk117(d, br);
                case Dat151RelType.Entity: return new Dat151Entity(d, br); //not sure about this
                case Dat151RelType.Unk47: return new Dat151Unk47(d, br);
                case Dat151RelType.Unk83: return new Dat151Unk83(d, br);
                case Dat151RelType.RadioDjSpeechAction: return new Dat151RadioDjSpeechAction(d, br);
                case Dat151RelType.ForceRadioTrackAction: return new Dat151ForceRadioTrackAction(d, br);
                case Dat151RelType.Unk78: return new Dat151Unk78(d, br);
                case Dat151RelType.RadioStations: return new Dat151RadioStations(d, br); //
                case Dat151RelType.RadioStation: return new Dat151RadioStation(d, br);
                case Dat151RelType.RadioMusic: return new Dat151RadioMusic(d, br);
                case Dat151RelType.RadioTrackList: return new Dat151RadioTrackList(d, br);
                case Dat151RelType.Unk49: return new Dat151Unk49(d, br);
                case Dat151RelType.Unk84: return new Dat151Unk84(d, br);
                case Dat151RelType.Unk86: return new Dat151Unk86(d, br);
                case Dat151RelType.Unk81: return new Dat151Unk81(d, br);
                case Dat151RelType.Unk55: return new Dat151Unk55(d, br);
                case Dat151RelType.ShoreLinePool: return new Dat151ShoreLinePool(d, br);
                case Dat151RelType.ShoreLineLake: return new Dat151ShoreLineLake(d, br);
                case Dat151RelType.ShoreLineRiver: return new Dat151ShoreLineRiver(d, br);
                case Dat151RelType.ShoreLineOcean: return new Dat151ShoreLineOcean(d, br);
                case Dat151RelType.ShoreLineList: return new Dat151ShoreLineList(d, br);

                case Dat151RelType.Unk114: return new Dat151Unk114(d, br);
                case Dat151RelType.VehicleEngineGranular: return new Dat151VehicleEngineGranular(d, br); //maybe not just vehicle
                case Dat151RelType.Vehicle: return new Dat151Vehicle(d, br);
                case Dat151RelType.VehicleEngine: return new Dat151VehicleEngine(d, br);
                case Dat151RelType.VehicleScannerParams: return new Dat151VehicleScannerParams(d, br); //maybe not just vehicle
                case Dat151RelType.StaticEmitter: return new Dat151StaticEmitter(d, br);
                case Dat151RelType.Weapon: return new Dat151Weapon(d, br);
                case Dat151RelType.Explosion: return new Dat151Explosion(d, br);
                case Dat151RelType.PedPVG: return new Dat151PedPVG(d, br); //maybe Ped Voice Group?

                //case Dat151RelType.Helicopter: return new Dat151Helicopter(d, br); //maybe
                //case Dat151RelType.SpeechParams: return new Dat151SpeechParams(d, br);
                //case Dat151RelType.Aeroplane: return new Dat151Aeroplane(d, br);
                //case Dat151RelType.AnimalParams: return new Dat151AnimalParams(d, br);
                //case Dat151RelType.Unk2: return new Dat151Unk2(d, br);
                //case Dat151RelType.Unk7: return new Dat151Unk7(d, br);
                //case Dat151RelType.Unk9: return new Dat151Unk9(d, br);
                //case Dat151RelType.Unk11: return new Dat151Unk11(d, br);
                //case Dat151RelType.Unk12: return new Dat151Unk12(d, br);
                //case Dat151RelType.Unk13: return new Dat151Unk13(d, br);
                //case Dat151RelType.Unk15: return new Dat151Unk15(d, br);
                //case Dat151RelType.Unk16: return new Dat151Unk16(d, br);
                //case Dat151RelType.Unk18: return new Dat151Unk18(d, br);
                //case Dat151RelType.Unk22: return new Dat151Unk22(d, br);
                //case Dat151RelType.Unk23: return new Dat151Unk23(d, br);
                //case Dat151RelType.Unk27: return new Dat151Unk27(d, br);
                //case Dat151RelType.Unk28: return new Dat151Unk28(d, br);
                //case Dat151RelType.Unk29: return new Dat151Unk29(d, br);
                //case Dat151RelType.Unk31: return new Dat151Unk31(d, br);
                //case Dat151RelType.Unk33: return new Dat151Unk33(d, br);
                //case Dat151RelType.Unk35: return new Dat151Unk35(d, br);
                //case Dat151RelType.Unk36: return new Dat151Unk36(d, br);
                //case Dat151RelType.Unk40: return new Dat151Unk40(d, br);
                //case Dat151RelType.Unk41: return new Dat151Unk41(d, br);
                //case Dat151RelType.Unk42: return new Dat151Unk42(d, br);
                //case Dat151RelType.Unk45: return new Dat151Unk45(d, br);
                //case Dat151RelType.Unk48: return new Dat151Unk48(d, br);
                //case Dat151RelType.Unk51: return new Dat151Unk51(d, br);
                //case Dat151RelType.Unk53: return new Dat151Unk53(d, br);
                //case Dat151RelType.Unk54: return new Dat151Unk54(d, br);
                //case Dat151RelType.Unk56: return new Dat151Unk56(d, br);
                //case Dat151RelType.Unk59: return new Dat151Unk59(d, br);
                //case Dat151RelType.Unk69: return new Dat151Unk69(d, br);
                //case Dat151RelType.Unk70: return new Dat151Unk70(d, br);
                //case Dat151RelType.Unk71: return new Dat151Unk71(d, br);
                //case Dat151RelType.Unk72: return new Dat151Unk72(d, br);
                //case Dat151RelType.Unk74: return new Dat151Unk74(d, br);
                //case Dat151RelType.Unk75: return new Dat151Unk75(d, br);
                //case Dat151RelType.Unk77: return new Dat151Unk77(d, br);
                //case Dat151RelType.Unk79: return new Dat151Unk79(d, br);
                //case Dat151RelType.Unk80: return new Dat151Unk80(d, br);
                //case Dat151RelType.Unk82: return new Dat151Unk82(d, br);
                //case Dat151RelType.Unk85: return new Dat151Unk85(d, br);
                //case Dat151RelType.Unk95: return new Dat151Unk95(d, br);
                //case Dat151RelType.Unk96: return new Dat151Unk96(d, br);
                //case Dat151RelType.Unk99: return new Dat151Unk99(d, br);
                //case Dat151RelType.Unk100: return new Dat151Unk100(d, br);
                //case Dat151RelType.Unk101: return new Dat151Unk101(d, br);
                //case Dat151RelType.Unk105: return new Dat151Unk105(d, br);
                //case Dat151RelType.Unk106: return new Dat151Unk106(d, br);
                //case Dat151RelType.Unk107: return new Dat151Unk107(d, br);
                //case Dat151RelType.Unk108: return new Dat151Unk108(d, br);
                //case Dat151RelType.Unk109: return new Dat151Unk109(d, br);
                //case Dat151RelType.Unk110: return new Dat151Unk110(d, br);
                //case Dat151RelType.Unk111: return new Dat151Unk111(d, br);
                //case Dat151RelType.Unk112: return new Dat151Unk112(d, br);
                //case Dat151RelType.Unk113: return new Dat151Unk113(d, br);
                //case Dat151RelType.Unk115: return new Dat151Unk115(d, br);
                //case Dat151RelType.Unk116: return new Dat151Unk116(d, br);
                //case Dat151RelType.Unk118: return new Dat151Unk118(d, br);
                //case Dat151RelType.Unk119: return new Dat151Unk119(d, br);
                //case Dat151RelType.Unk121: return new Dat151Unk121(d, br);

                default:
                    return new Dat151RelData(d, br);
            }
        }


        public RelData CreateRelData(RelDatFileType relType, int dataType)
        {
            RelData d = null;
            switch (relType)
            {
                case RelDatFileType.Dat54DataEntries:
                    switch ((Dat54SoundType)dataType)
                    {
                        case Dat54SoundType.LoopingSound: return new Dat54LoopingSound(this);
                        case Dat54SoundType.EnvelopeSound: return new Dat54EnvelopeSound(this);
                        case Dat54SoundType.TwinLoopSound: return new Dat54TwinLoopSound(this);
                        case Dat54SoundType.SpeechSound: return new Dat54SpeechSound(this);
                        case Dat54SoundType.OnStopSound: return new Dat54OnStopSound(this);
                        case Dat54SoundType.WrapperSound: return new Dat54WrapperSound(this);
                        case Dat54SoundType.SequentialSound: return new Dat54SequentialSound(this);
                        case Dat54SoundType.StreamingSound: return new Dat54StreamingSound(this);
                        case Dat54SoundType.RetriggeredOverlappedSound: return new Dat54RetriggeredOverlappedSound(this);
                        case Dat54SoundType.CrossfadeSound: return new Dat54CrossfadeSound(this);
                        case Dat54SoundType.CollapsingStereoSound: return new Dat54CollapsingStereoSound(this);
                        case Dat54SoundType.SimpleSound: return new Dat54SimpleSound(this);
                        case Dat54SoundType.MultitrackSound: return new Dat54MultitrackSound(this);
                        case Dat54SoundType.RandomizedSound: return new Dat54RandomizedSound(this);
                        case Dat54SoundType.EnvironmentSound: return new Dat54EnvironmentSound(this);
                        case Dat54SoundType.DynamicEntitySound: return new Dat54DynamicEntitySound(this);
                        case Dat54SoundType.SequentialOverlapSound: return new Dat54SequentialOverlapSound(this);
                        case Dat54SoundType.ModularSynthSound: return new Dat54ModularSynthSound(this);
                        case Dat54SoundType.GranularSound: return new Dat54GranularSound(this);
                        case Dat54SoundType.DirectionalSound: return new Dat54DirectionalSound(this);
                        case Dat54SoundType.KineticSound: return new Dat54KineticSound(this);
                        case Dat54SoundType.SwitchSound: return new Dat54SwitchSound(this);
                        case Dat54SoundType.VariableCurveSound: return new Dat54VariableCurveSound(this);
                        case Dat54SoundType.VariablePrintValueSound: return new Dat54VariablePrintValueSound(this);
                        case Dat54SoundType.VariableBlockSound: return new Dat54VariableBlockSound(this);
                        case Dat54SoundType.IfSound: return new Dat54IfSound(this);
                        case Dat54SoundType.MathOperationSound: return new Dat54MathOperationSound(this);
                        case Dat54SoundType.ParameterTransformSound: return new Dat54ParameterTransformSound(this);
                        case Dat54SoundType.FluctuatorSound: return new Dat54FluctuatorSound(this);
                        case Dat54SoundType.AutomationSound: return new Dat54AutomationSound(this);
                        case Dat54SoundType.ExternalStreamSound: return new Dat54ExternalStreamSound(this);
                        case Dat54SoundType.SoundSet: return new Dat54SoundSet(this);
                        case Dat54SoundType.Unknown: return new Dat54UnknownSound(this);
                        case Dat54SoundType.Unknown2: return new Dat54UnknownSound2(this);
                        case Dat54SoundType.SoundList: return new Dat54SoundList(this);
                        default:
                            return new Dat54Sound(this, (Dat54SoundType)d.TypeID); //shouldn't get here
                    }
                case RelDatFileType.Dat149:
                case RelDatFileType.Dat150:
                case RelDatFileType.Dat151:
                    switch ((Dat151RelType)dataType)
                    {
                        case Dat151RelType.AmbientEmitterList: return new Dat151AmbientEmitterList(this);
                        case Dat151RelType.AmbientZone: return new Dat151AmbientZone(this);
                        case Dat151RelType.AmbientEmitter: return new Dat151AmbientEmitter(this);
                        case Dat151RelType.AmbientZoneList: return new Dat151AmbientZoneList(this);
                        case Dat151RelType.Collision: return new Dat151Collision(this); //maybe for vehicle
                        case Dat151RelType.WeaponAudioItem: return new Dat151WeaponAudioItem(this);
                        case Dat151RelType.StartTrackAction: return new Dat151StartTrackAction(this);
                        case Dat151RelType.StopTrackAction: return new Dat151StopTrackAction(this);
                        case Dat151RelType.Mood: return new Dat151Mood(this);
                        case Dat151RelType.SetMoodAction: return new Dat151SetMoodAction(this);
                        case Dat151RelType.PlayerAction: return new Dat151PlayerAction(this);
                        case Dat151RelType.StartOneShotAction: return new Dat151StartOneShotAction(this);
                        case Dat151RelType.StopOneShotAction: return new Dat151StopOneShotAction(this);
                        case Dat151RelType.FadeOutRadioAction: return new Dat151FadeOutRadioAction(this);
                        case Dat151RelType.FadeInRadioAction: return new Dat151FadeInRadioAction(this);
                        case Dat151RelType.Mod: return new Dat151Mod(this);
                        case Dat151RelType.Interior: return new Dat151Interior(this);
                        case Dat151RelType.InteriorRoom: return new Dat151InteriorRoom(this);
                        case Dat151RelType.Unk117: return new Dat151Unk117(this);
                        case Dat151RelType.Entity: return new Dat151Entity(this); //not sure about this
                        case Dat151RelType.Unk47: return new Dat151Unk47(this);
                        case Dat151RelType.Unk83: return new Dat151Unk83(this);
                        case Dat151RelType.RadioDjSpeechAction: return new Dat151RadioDjSpeechAction(this);
                        case Dat151RelType.ForceRadioTrackAction: return new Dat151ForceRadioTrackAction(this);
                        case Dat151RelType.Unk78: return new Dat151Unk78(this);
                        case Dat151RelType.RadioStations: return new Dat151RadioStations(this); //
                        case Dat151RelType.RadioStation: return new Dat151RadioStation(this);
                        case Dat151RelType.RadioMusic: return new Dat151RadioMusic(this);
                        case Dat151RelType.RadioTrackList: return new Dat151RadioTrackList(this);
                        case Dat151RelType.Unk49: return new Dat151Unk49(this);
                        case Dat151RelType.Unk84: return new Dat151Unk84(this);
                        case Dat151RelType.Unk86: return new Dat151Unk86(this);
                        case Dat151RelType.Unk81: return new Dat151Unk81(this);
                        case Dat151RelType.Unk55: return new Dat151Unk55(this);
                        case Dat151RelType.ShoreLinePool: return new Dat151ShoreLinePool(this);
                        case Dat151RelType.ShoreLineLake: return new Dat151ShoreLineLake(this);
                        case Dat151RelType.ShoreLineRiver: return new Dat151ShoreLineRiver(this);
                        case Dat151RelType.ShoreLineOcean: return new Dat151ShoreLineOcean(this);
                        case Dat151RelType.ShoreLineList: return new Dat151ShoreLineList(this);
                        case Dat151RelType.Unk114: return new Dat151Unk114(this);
                        case Dat151RelType.VehicleEngineGranular: return new Dat151VehicleEngineGranular(this); //maybe not just vehicle
                        case Dat151RelType.Vehicle: return new Dat151Vehicle(this);
                        case Dat151RelType.VehicleEngine: return new Dat151VehicleEngine(this);
                        case Dat151RelType.VehicleScannerParams: return new Dat151VehicleScannerParams(this); //maybe not just vehicle
                        case Dat151RelType.StaticEmitter: return new Dat151StaticEmitter(this);
                        case Dat151RelType.Weapon: return new Dat151Weapon(this);
                        case Dat151RelType.Explosion: return new Dat151Explosion(this);
                        case Dat151RelType.PedPVG: return new Dat151PedPVG(this); //maybe Ped Voice Group?

                        //case Dat151RelType.Helicopter: return new Dat151Helicopter(this); //maybe
                        //case Dat151RelType.SpeechParams: return new Dat151SpeechParams(this);
                        //case Dat151RelType.Aeroplane: return new Dat151Aeroplane(this);
                        //case Dat151RelType.AnimalParams: return new Dat151AnimalParams(this);
                        //case Dat151RelType.Unk2: return new Dat151Unk2(this);
                        //case Dat151RelType.Unk7: return new Dat151Unk7(this);
                        //case Dat151RelType.Unk9: return new Dat151Unk9(this);
                        //case Dat151RelType.Unk11: return new Dat151Unk11(this);
                        //case Dat151RelType.Unk12: return new Dat151Unk12(this);
                        //case Dat151RelType.Unk13: return new Dat151Unk13(this);
                        //case Dat151RelType.Unk15: return new Dat151Unk15(this);
                        //case Dat151RelType.Unk16: return new Dat151Unk16(this);
                        //case Dat151RelType.Unk18: return new Dat151Unk18(this);
                        //case Dat151RelType.Unk22: return new Dat151Unk22(this);
                        //case Dat151RelType.Unk23: return new Dat151Unk23(this);
                        //case Dat151RelType.Unk27: return new Dat151Unk27(this);
                        //case Dat151RelType.Unk28: return new Dat151Unk28(this);
                        //case Dat151RelType.Unk29: return new Dat151Unk29(this);
                        //case Dat151RelType.Unk31: return new Dat151Unk31(this);
                        //case Dat151RelType.Unk33: return new Dat151Unk33(this);
                        //case Dat151RelType.Unk35: return new Dat151Unk35(this);
                        //case Dat151RelType.Unk36: return new Dat151Unk36(this);
                        //case Dat151RelType.Unk40: return new Dat151Unk40(this);
                        //case Dat151RelType.Unk41: return new Dat151Unk41(this);
                        //case Dat151RelType.Unk42: return new Dat151Unk42(this);
                        //case Dat151RelType.Unk45: return new Dat151Unk45(this);
                        //case Dat151RelType.Unk48: return new Dat151Unk48(this);
                        //case Dat151RelType.Unk51: return new Dat151Unk51(this);
                        //case Dat151RelType.Unk53: return new Dat151Unk53(this);
                        //case Dat151RelType.Unk54: return new Dat151Unk54(this);
                        //case Dat151RelType.Unk56: return new Dat151Unk56(this);
                        //case Dat151RelType.Unk59: return new Dat151Unk59(this);
                        //case Dat151RelType.Unk69: return new Dat151Unk69(this);
                        //case Dat151RelType.Unk70: return new Dat151Unk70(this);
                        //case Dat151RelType.Unk71: return new Dat151Unk71(this);
                        //case Dat151RelType.Unk72: return new Dat151Unk72(this);
                        //case Dat151RelType.Unk74: return new Dat151Unk74(this);
                        //case Dat151RelType.Unk75: return new Dat151Unk75(this);
                        //case Dat151RelType.Unk77: return new Dat151Unk77(this);
                        //case Dat151RelType.Unk79: return new Dat151Unk79(this);
                        //case Dat151RelType.Unk80: return new Dat151Unk80(this);
                        //case Dat151RelType.Unk82: return new Dat151Unk82(this);
                        //case Dat151RelType.Unk85: return new Dat151Unk85(this);
                        //case Dat151RelType.Unk95: return new Dat151Unk95(this);
                        //case Dat151RelType.Unk96: return new Dat151Unk96(this);
                        //case Dat151RelType.Unk99: return new Dat151Unk99(this);
                        //case Dat151RelType.Unk100: return new Dat151Unk100(this);
                        //case Dat151RelType.Unk101: return new Dat151Unk101(this);
                        //case Dat151RelType.Unk105: return new Dat151Unk105(this);
                        //case Dat151RelType.Unk106: return new Dat151Unk106(this);
                        //case Dat151RelType.Unk107: return new Dat151Unk107(this);
                        //case Dat151RelType.Unk108: return new Dat151Unk108(this);
                        //case Dat151RelType.Unk109: return new Dat151Unk109(this);
                        //case Dat151RelType.Unk110: return new Dat151Unk110(this);
                        //case Dat151RelType.Unk111: return new Dat151Unk111(this);
                        //case Dat151RelType.Unk112: return new Dat151Unk112(this);
                        //case Dat151RelType.Unk113: return new Dat151Unk113(this);
                        //case Dat151RelType.Unk115: return new Dat151Unk115(this);
                        //case Dat151RelType.Unk116: return new Dat151Unk116(this);
                        //case Dat151RelType.Unk118: return new Dat151Unk118(this);
                        //case Dat151RelType.Unk119: return new Dat151Unk119(this);
                        //case Dat151RelType.Unk121: return new Dat151Unk121(this);

                        default:
                            return new Dat151RelData(this, (Dat151RelType)dataType);
                    }
                default:
                    d = new RelData(this);
                    d.TypeID = (byte)dataType;
                    return d;
            }
        }



        private void BuildNameTable()
        {
            //TODO!
            //need to do this before building the data block since nametable offsets are in there!






            if (NameTable != null)
            {
                NameTableCount = (uint)NameTable.Length;
                uint ntlength = 4 + (4 * NameTableCount);
                foreach (var name in NameTable)
                {
                    ntlength += (uint)name.Length + 1;
                }
                if ((NameTableLength != ntlength)&&(NameTableLength!=0))
                { }
                NameTableLength = ntlength;
            }
            else
            {
                if ((NameTableLength != 4)&& (NameTableLength != 0))
                { }
                NameTableCount = 0;
                NameTableLength = 4;
            }


        }
        private void BuildDataBlock()
        {
            if (RelDatas == null) return;
            if (RelDatasSorted == null) return;


            switch (RelType)
            {
                case RelDatFileType.Dat4://TODO!
                    return;
            }



            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);

            bw.Write(DataUnkVal);


            RelData lastrd = null;//debug

            for (int i = 0; i < RelDatasSorted.Length; i++)
            {

                var rd = RelDatasSorted[i];

                switch (RelType)
                {
                    case RelDatFileType.Dat10ModularSynth:
                        while ((ms.Position & 3) != 0) bw.Write((byte)0); //align these to nearest 4 bytes
                        break;
                    case RelDatFileType.Dat15DynamicMixer:
                        switch (rd.TypeID)
                        {
                            case 0:
                            case 6:
                            case 5:
                            case 7:
                            case 8:
                                while ((ms.Position & 3) != 0) bw.Write((byte)0); //align these to nearest 4 bytes
                                break;
                            default:
                                break;
                        }
                        break;
                    case RelDatFileType.Dat149:
                    case RelDatFileType.Dat150:
                    case RelDatFileType.Dat151:
                        switch ((Dat151RelType)rd.TypeID)//must be a better way of doing this!
                        {
                            case Dat151RelType.AmbientEmitter:
                            case Dat151RelType.AmbientZone:
                            case Dat151RelType.Unk101:
                            case Dat151RelType.Unk35:
                                while ((ms.Position & 0xF) != 0) bw.Write((byte)0); //align to nearest 16 bytes
                                break;
                            case Dat151RelType.Mood:
                            case Dat151RelType.Unk70:
                            case Dat151RelType.Unk29:
                            case Dat151RelType.SpeechParams:
                            case Dat151RelType.Unk11:
                            case Dat151RelType.Unk41:
                            case Dat151RelType.Unk2:
                            case Dat151RelType.AmbientEmitterList:
                            case Dat151RelType.Weapon:
                            case Dat151RelType.Vehicle:
                            case Dat151RelType.StopTrackAction:
                                while ((ms.Position & 3) != 0) bw.Write((byte)0); //align these to nearest 4 bytes
                                break;
                        }
                        break;
                    //case RelDatFileType.Dat4://TODO!
                    //case RelDatFileType.Dat54DataEntries://TODO!
                    //    //default://TODO..?
                    //    return;
                }


                var pos = ms.Position;
                if ((ms.Position != rd.DataOffset) && (rd.DataOffset != 0))
                { }
                rd.DataOffset = (uint)ms.Position;
                rd.Write(bw);
                var lengthwritten = ms.Position - pos;
                if ((lengthwritten != rd.DataLength) && (rd.DataLength != 0))
                { }
                rd.DataLength = (uint)lengthwritten;

                lastrd = rd;
            }

            var buf = new byte[ms.Length];
            ms.Position = 0;
            ms.Read(buf, 0, buf.Length);

            if ((DataBlock!=null)&&(DataBlock.Length != buf.Length))
            { }

            DataBlock = buf;

            DataLength = (uint)(DataBlock?.Length ?? 0);
        }
        private void BuildIndex()
        {
            if (RelDatas == null) return;
            if (RelDatasSorted == null) return;

            switch (RelType)
            {
                case RelDatFileType.Dat4://TODO!
                    return;
            }


            //for the correct index ordering, needs to be in order of hashes, but with bits rotated right by 8 (why!?)
            var sorted = RelDatasSorted.ToList();
            switch (RelType)
            {
                case RelDatFileType.Dat15DynamicMixer:
                    //don't sort? sort alphabetically?
                    //break;
                case RelDatFileType.Dat149:
                case RelDatFileType.Dat150:
                case RelDatFileType.Dat151:
                case RelDatFileType.Dat10ModularSynth:
                case RelDatFileType.Dat22Categories:
                case RelDatFileType.Dat16Curves:
                case RelDatFileType.Dat54DataEntries:
                    sorted.Sort((a, b) => 
                    {
                        var ah = (uint)a.NameHash;
                        var bh = (uint)b.NameHash;
                        var av = (ah >> 8) | (ah << 24);
                        var bv = (bh >> 8) | (bh << 24);
                        return av.CompareTo(bv);
                    });
                    break;
                default:
                    sorted.Sort((a, b) => { return ((uint)a.NameHash).CompareTo((uint)b.NameHash); });
                    break;
            }
            RelDatas = sorted.ToArray();



            var hashes = new RelIndexHash[RelDatas.Length];
            for (int i = 0; i < RelDatas.Length; i++)
            {
                var rd = RelDatas[i];
                hashes[i] = new RelIndexHash() { Name = rd.NameHash, Offset = rd.DataOffset, Length = rd.DataLength };
            }
            //if (hashes.Length != IndexHashes.Length)
            //{ }

            IndexHashes = hashes;
            //IndexCount = (uint)hashes.Length;

            if ((RelType == RelDatFileType.Dat4) && (NameTableLength == 4))
            {
                IndexCount = (uint)(IndexStrings?.Length ?? 0);
            }
            else
            {
                IndexCount = (uint)(IndexHashes?.Length ?? 0);
            }
        }
        private void BuildHashTable()
        {
            if (RelDatasSorted == null) return;

            switch (RelType)
            {
                case RelDatFileType.Dat4://TODO!
                    return;
            }

            var htoffsets = new List<uint>();
            foreach (var rd in RelDatasSorted)
            {
                var offsets = rd.GetHashTableOffsets();
                if (offsets == null) continue;
                var rdoffset = rd.DataOffset + 8;
                var rs = rd as RelSound;
                if (rs?.Header != null)
                {
                    rdoffset += 1 + rs.Header.CalcHeaderLength();
                }
                else
                {
                    rdoffset += 4; //typeid + nt offset
                }
                for (int i = 0; i < offsets.Length; i++)
                {
                    htoffsets.Add(rdoffset + offsets[i]);

                    int idx = htoffsets.Count - 1;
                    if ((HashTableOffsets != null) && (idx < HashTableOffsets.Length))
                    {
                        if (htoffsets[idx] != HashTableOffsets[idx])
                        { }
                    }
                }
            }
            if (htoffsets.Count > 0)
            {
                if (HashTableOffsets != null)
                {
                    if (HashTableOffsets.Length != htoffsets.Count)
                    { }
                    else
                    {
                        for (int i = 0; i < htoffsets.Count; i++)
                        {
                            if (htoffsets[i] != HashTableOffsets[i])
                            { }
                        }
                    }
                }
                HashTableOffsets = htoffsets.ToArray();
            }
            else
            {
                HashTableOffsets = null;
            }

            HashTableCount = (uint)(HashTableOffsets?.Length ?? 0);
        }
        private void BuildPackTable()
        {
            switch (RelType)
            {
                case RelDatFileType.Dat4://TODO!
                    return;
            }

            var ptoffsets = new List<uint>();
            foreach (var rd in RelDatasSorted)
            {
                var offsets = rd.GetPackTableOffsets();
                if (offsets == null) continue;
                var rdoffset = rd.DataOffset + 8;
                var rs = rd as RelSound;
                if (rs?.Header != null)
                {
                    rdoffset += 1 + rs.Header.CalcHeaderLength();
                }
                else
                {
                    rdoffset += 4; //typeid + nt offset
                }
                for (int i = 0; i < offsets.Length; i++)
                {
                    ptoffsets.Add(rdoffset + offsets[i]);
                }
            }
            if (ptoffsets.Count > 0)
            {
                if (PackTableOffsets != null)
                {
                    if (PackTableOffsets.Length != ptoffsets.Count)
                    { }
                    else
                    {
                        for (int i = 0; i < ptoffsets.Count; i++)
                        {
                            if (ptoffsets[i] != PackTableOffsets[i])
                            { }
                        }
                    }
                }
                PackTableOffsets = ptoffsets.ToArray();
            }
            else
            {
                PackTableOffsets = null;
            }

            PackTableCount = (uint)(PackTableOffsets?.Length ?? 0);
        }



        private void BuildHashMaps()
        {
            //for discovering "HashTable" offsets

            var relType = RelType;
            switch (RelType)
            {
                case RelDatFileType.Dat149:
                case RelDatFileType.Dat150://treat these same as 151
                case RelDatFileType.Dat151:
                    relType = RelDatFileType.Dat151;
                    break;
                default:
                    break;
            }

            if ((relType != RelDatFileType.Dat54DataEntries))// && (relType != RelDatFileType.Dat4))
            { return; }


            if (HashTableOffsets != null)
            {
                foreach (var htoffset in HashTableOffsets)
                {
                    var dboffset = htoffset - 8;
                    for (int i = 0; i < RelDatasSorted.Length; i++)
                    {
                        var rd = RelDatasSorted[i];

                        if ((dboffset >= rd.DataOffset) && (dboffset < rd.DataOffset + rd.DataLength))
                        {
                            var rdoffset = rd.DataOffset;
                            var rs = rd as RelSound;
                            if (rs != null)
                            {
                                rdoffset += 1 + rs.Header.HeaderLength;
                            }
                            var key = new HashesMapKey()
                            {
                                FileType = relType,
                                ItemType = rd.TypeID,
                                IsContainer = false
                            };
                            var val = new HashesMapValue()
                            {
                                Item = rd,
                                Hash = BitConverter.ToUInt32(DataBlock, (int)dboffset),
                                Offset = dboffset - rdoffset,
                                Count = 1
                            };
                            AddHashesMapItem(ref key, val);
                            break;
                        }
                    }
                }
            }
            if (PackTableOffsets != null)
            {
                foreach (var wcoffset in PackTableOffsets)
                {
                    var dboffset = wcoffset - 8;
                    for (int i = 0; i < RelDatasSorted.Length; i++)
                    {
                        var rd = RelDatasSorted[i];
                        if ((dboffset >= rd.DataOffset) && (dboffset < rd.DataOffset + rd.DataLength))
                        {
                            var rdoffset = rd.DataOffset;
                            var rs = rd as RelSound;
                            if (rs != null)
                            {
                                rdoffset += 1 + rs.Header.HeaderLength;
                            }
                            var key = new HashesMapKey()
                            {
                                FileType = relType,
                                ItemType = rd.TypeID,
                                IsContainer = true
                            };
                            var val = new HashesMapValue()
                            {
                                Item = rd,
                                Hash = BitConverter.ToUInt32(DataBlock, (int)dboffset),
                                Offset = dboffset - rdoffset,
                                Count = 1
                            };
                            AddHashesMapItem(ref key, val);
                            break;
                        }
                    }
                }
            }


        }
        public struct HashesMapKey
        {
            public RelDatFileType FileType { get; set; }
            public uint ItemType { get; set; }
            public bool IsContainer { get; set; }

            public override string ToString()
            {
                var cstr = IsContainer ? "Container: " : "";
                var fcstr = cstr + FileType.ToString() + ": ";
                switch (FileType)
                {
                    case RelDatFileType.Dat54DataEntries:
                        return fcstr + ((Dat54SoundType)ItemType).ToString();
                    case RelDatFileType.Dat149:
                    case RelDatFileType.Dat150:
                    case RelDatFileType.Dat151:
                        return fcstr + ((Dat151RelType)ItemType).ToString();
                }

                return fcstr + ItemType.ToString();
            }
        }
        public class HashesMapValue
        {
            public RelData Item { get; set; }
            public MetaHash Hash { get; set; }
            public uint Offset { get; set; }
            public uint Count { get; set; }

            public override string ToString()
            {
                return Offset.ToString() + ": " + Count.ToString();
            }
        }
        public static Dictionary<HashesMapKey, List<HashesMapValue>> HashesMap { get; set; } = new Dictionary<HashesMapKey, List<HashesMapValue>>();
        private static void AddHashesMapItem(ref HashesMapKey key, HashesMapValue val)
        {
            List<HashesMapValue> values = null;
            if (!HashesMap.TryGetValue(key, out values))
            {
                values = new List<HashesMapValue>();
                HashesMap[key] = values;
            }
            if (values != null)
            {
                foreach (var xval in values)
                {
                    if (xval.Offset == val.Offset)
                    {
                        xval.Count++;
                        return;//same key, same offset, it's a match...
                    }
                }
                values.Add(val);
            }
            else
            { }
        }




        public byte[] Save()
        {
            
            BuildNameTable();
            BuildDataBlock();
            BuildIndex();
            BuildHashTable();
            BuildPackTable();


            if (DataBlock == null) return null;



            //write the file data.

            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);


            bw.Write((uint)RelType);
            bw.Write(DataLength);
            bw.Write(DataBlock);

            bw.Write(NameTableLength);
            bw.Write(NameTableCount);
            if (NameTableCount > 0)
            {
                uint offset = 0;
                foreach (var name in NameTable)
                {
                    bw.Write(offset);
                    offset += (uint)name.Length + 1;
                }
                foreach (var name in NameTable)
                {
                    foreach (var c in name)
                    {
                        bw.Write(c);
                    }
                    bw.Write((byte)0);
                }

            }

            bw.Write(IndexCount);
            if (IndexCount > 0)
            {
                if ((RelType == RelDatFileType.Dat4) && (NameTableLength == 4))//audioconfig.dat4.rel    //checking NameTableLength here doesn't make sense!
                {
                    bw.Write(IndexStringFlags); //should be 2524..? could be a length?
                    for (uint i = 0; i < IndexCount; i++)
                    {
                        var ristr = IndexStrings[i];
                        var name = ristr.Name;
                        bw.Write((byte)name.Length);
                        for (int j = 0; j < name.Length; j++)
                        {
                            bw.Write((byte)name[j]);
                        }
                        bw.Write(ristr.Offset);
                        bw.Write(ristr.Length);
                    }
                }
                else //for all other .rel files...
                {
                    for (uint i = 0; i < IndexCount; i++)
                    {
                        var rihash = IndexHashes[i];
                        bw.Write(rihash.Name);
                        bw.Write(rihash.Offset);
                        bw.Write(rihash.Length);
                    }
                }
            }

            bw.Write(HashTableCount);
            if (HashTableCount != 0)
            {
                for (uint i = 0; i < HashTableCount; i++)
                {
                    bw.Write(HashTableOffsets[i]);
                }
            }

            bw.Write(PackTableCount);
            if (PackTableCount != 0)
            {
                for (uint i = 0; i < PackTableCount; i++)
                {
                    bw.Write(PackTableOffsets[i]);
                }
            }


            var buf = new byte[ms.Length];
            ms.Position = 0;
            ms.Read(buf, 0, buf.Length);
            return buf;

        }







        public void AddRelData(RelData d)
        {
            var newRelDatas = new List<RelData>();
            var newRelDatasSorted = new List<RelData>();

            if (RelDatas != null) newRelDatas.AddRange(RelDatas);
            if (RelDatasSorted != null) newRelDatasSorted.AddRange(RelDatasSorted);

            newRelDatas.Add(d);
            newRelDatasSorted.Add(d);

            RelDatas = newRelDatas.ToArray();
            RelDatasSorted = newRelDatasSorted.ToArray();
            //RelDataDict[d.NameHash] = d;
        }
        public bool RemoveRelData(RelData d)
        {
            var newRelDatas = new List<RelData>();
            var newRelDatasSorted = new List<RelData>();

            if (RelDatas != null)
            {
                foreach (var relData in RelDatas)
                {
                    if (relData != d)
                    {
                        newRelDatas.Add(relData);
                    }
                }
            }
            if (RelDatasSorted != null)
            {
                foreach (var relData in RelDatasSorted)
                {
                    if (relData != d)
                    {
                        newRelDatasSorted.Add(relData);
                    }
                }
            }

            if (newRelDatas.Count < RelDatas.Length)
            {
                RelDatas = newRelDatas.ToArray();
                RelDatasSorted = newRelDatasSorted.ToArray();
                RelDataDict.Remove(d.NameHash);
                return true;
            }

            return false;
        }



        public override string ToString()
        {
            return Name;
        }
    }

    [TC(typeof(EXP))] public struct RelIndexHash
    {
        public MetaHash Name { get; set; }
        public uint Offset { get; set; }
        public uint Length { get; set; }

        public override string ToString()
        {
            return Name.ToString() + ", " + Offset.ToString() + ", " + Length.ToString();
        }
    }


    [TC(typeof(EXP))] public struct RelIndexString
    {
        public string Name { get; set; }
        public uint Offset { get; set; }
        public uint Length { get; set; }

        public override string ToString()
        {
            return Name + ", " + Offset.ToString() + ", " + Length.ToString();
        }
    }



    [TC(typeof(EXP))] public class RelData
    {
        public MetaHash NameHash { get; set; }
        public string Name { get; set; }
        public uint DataOffset { get; set; }
        public uint DataLength { get; set; }
        public byte[] Data { get; set; }
        public byte TypeID { get; set; }

        public RelFile Rel { get; set; }

        public RelData(RelFile rel) { Rel = rel; }
        public RelData(RelData d)
        {
            NameHash = d.NameHash;
            Name = d.Name;
            DataOffset = d.DataOffset;
            DataLength = d.DataLength;
            Data = d.Data;
            TypeID = d.TypeID;
            Rel = d.Rel;
        }

        public void ReadType(BinaryReader br)
        {
            TypeID = br.ReadByte();
        }

        public virtual uint[] GetHashTableOffsets()
        {
            return null;
        }
        public virtual uint[] GetPackTableOffsets()
        {
            return null;
        }

        public virtual void Write(BinaryWriter bw)
        {
            bw.Write(Data); //fallback for default byte array data writing...
        }


        public virtual void WriteXml(StringBuilder sb, int indent)
        {
            //default fallback to write raw data to XML...

            RelXml.WriteRawArray(sb, Data, indent, "RawData", "", RelXml.FormatHexByte, 16);

        }

        public virtual void ReadXml(XmlNode node)
        {
            var rawnode = node.SelectSingleNode("RawData");
            if (rawnode != null)
            {
                Data = Xml.GetRawByteArray(rawnode);
                DataLength = (uint)Data.Length;
            }
        }


        public string GetNameString()
        {
            return (string.IsNullOrEmpty(Name)) ? NameHash.ToString() : Name;
        }
        public string GetBaseString()
        {
            return DataOffset.ToString() + ", " + DataLength.ToString() + ": " + GetNameString();
        }
        public override string ToString()
        {
            return GetBaseString() + ": " + TypeID.ToString();
        }

        public static bool Bit(uint f, int b)
        {
            return ((f & (1u << b)) != 0); //just for handyness... maybe move this?
        }
        public static bool BadF(float f)
        {
            return ((f < -15000) || (f > 15000));
        }
    }

    [TC(typeof(EXP))] public class RelSoundHeader
    {
        public FlagsUint Flags { get; set; }

        public FlagsUint Flags2 { get; set; }
        public ushort Unk01 { get; set; }
        public ushort Unk02 { get; set; }
        public ushort Unk03 { get; set; } //0xD-0xF
        public ushort Unk04 { get; set; } //0xF-0x11
        public ushort Unk05 { get; set; } //0x11-0x13
        public ushort Unk06 { get; set; } //0x13-0x15
        public ushort Unk07 { get; set; } //0x15-0x17
        public ushort Unk08 { get; set; } //0x17-0x19
        public ushort Unk09 { get; set; } //0x19-0x1B
        public int UnkInt1 { get; set; } //0x1B-0x1F
        public int UnkInt2 { get; set; } //0x1F-0x23
        public ushort Unk10 { get; set; } //0x23-0x25
        public ushort Unk11 { get; set; } //0x25-0x27
        public ushort Unk12 { get; set; } //0x27-0x29
        public MetaHash CategoryHash { get; set; } //0x29-0x2D
        public ushort Unk14 { get; set; } //0x2D-0x2F
        public ushort Unk15 { get; set; } //0x2F-0x31
        public ushort Unk16 { get; set; } //0x31-0x33
        public ushort Unk17 { get; set; } //0x33-0x35
        public MetaHash UnkHash3 { get; set; } //0x35-0x39
        public ushort Unk18 { get; set; } //0x39-0x3B
        public byte Unk19 { get; set; } //0x3B-0x3C
        public byte Unk20 { get; set; } //0x3C-0x3D
        public byte Unk21 { get; set; } //0x3D-0x3E
        public MetaHash UnkHash4 { get; set; } //0x3E-0x42
        public MetaHash UnkHash5 { get; set; } //0x42-0x46
        public ushort Unk22 { get; set; } //0x46-0x48
        public ushort Unk23 { get; set; } //0x48-0x4A
        public ushort Unk24 { get; set; } //0x4A-0x4C
        public ushort Unk25 { get; set; } //0x4A-0x4C
        public ushort Unk26 { get; set; } //0x4A-0x4C

        public uint HeaderLength { get; set; } = 0;


        public RelSoundHeader(XmlNode node)
        {
            ReadXml(node);
            HeaderLength = CalcHeaderLength();
        }
        public RelSoundHeader(BinaryReader br)
        {
            var pos = br.BaseStream.Position;

            Flags = br.ReadUInt32();

            //if (Flags.Value != 0xAAAAAAAA)
            if ((Flags & 0xFF) != 0xAA)
            {
                if (Bit(0)) Flags2 = br.ReadUInt32();
                if (Bit(1)) Unk01 = br.ReadUInt16();
                if (Bit(2)) Unk02 = br.ReadUInt16();
                if (Bit(3)) Unk03 = br.ReadUInt16();
                if (Bit(4)) Unk04 = br.ReadUInt16();
                if (Bit(5)) Unk05 = br.ReadUInt16();
                if (Bit(6)) Unk06 = br.ReadUInt16();
                if (Bit(7)) Unk07 = br.ReadUInt16();
            }
            if ((Flags & 0xFF00) != 0xAA00)
            {
                if (Bit(8)) Unk08 = br.ReadUInt16();
                if (Bit(9)) Unk09 = br.ReadUInt16();
                if (Bit(10)) UnkInt1 = br.ReadInt32();
                if (Bit(11)) UnkInt2 = br.ReadInt32();
                if (Bit(12)) Unk10 = br.ReadUInt16();
                if (Bit(13)) Unk11 = br.ReadUInt16();
                if (Bit(14)) Unk12 = br.ReadUInt16();
                if (Bit(15)) CategoryHash = br.ReadUInt32();
            }
            if ((Flags & 0xFF0000) != 0xAA0000)
            {
                if (Bit(16)) Unk14 = br.ReadUInt16();
                if (Bit(17)) Unk15 = br.ReadUInt16();
                if (Bit(18)) Unk16 = br.ReadUInt16();
                if (Bit(19)) Unk17 = br.ReadUInt16();
                if (Bit(20)) UnkHash3 = br.ReadUInt32();
                if (Bit(21)) Unk18 = br.ReadUInt16();
                if (Bit(22)) Unk19 = br.ReadByte();
                if (Bit(23)) Unk20 = br.ReadByte();
            }
            if ((Flags & 0xFF000000) != 0xAA000000)
            {
                if (Bit(24)) Unk21 = br.ReadByte();
                if (Bit(25)) UnkHash4 = br.ReadUInt32();
                if (Bit(26)) UnkHash5 = br.ReadUInt32();
                if (Bit(27)) Unk22 = br.ReadUInt16();
                if (Bit(28)) Unk23 = br.ReadUInt16();
                if (Bit(29)) Unk24 = br.ReadUInt16();
                if (Bit(30)) Unk25 = br.ReadUInt16(); //maybe not
                if (Bit(31)) Unk26 = br.ReadUInt16(); //maybe not
            }

            HeaderLength = (uint)(br.BaseStream.Position - pos);

        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Flags);

            //if (Flags.Value != 0xAAAAAAAA)
            if ((Flags & 0xFF) != 0xAA)
            {
                if (Bit(0)) bw.Write(Flags2);
                if (Bit(1)) bw.Write(Unk01);
                if (Bit(2)) bw.Write(Unk02);
                if (Bit(3)) bw.Write(Unk03);
                if (Bit(4)) bw.Write(Unk04);
                if (Bit(5)) bw.Write(Unk05);
                if (Bit(6)) bw.Write(Unk06);
                if (Bit(7)) bw.Write(Unk07);
            }
            if ((Flags & 0xFF00) != 0xAA00)
            {
                if (Bit(8)) bw.Write(Unk08);
                if (Bit(9)) bw.Write(Unk09);
                if (Bit(10)) bw.Write(UnkInt1);
                if (Bit(11)) bw.Write(UnkInt2);
                if (Bit(12)) bw.Write(Unk10);
                if (Bit(13)) bw.Write(Unk11);
                if (Bit(14)) bw.Write(Unk12);
                if (Bit(15)) bw.Write(CategoryHash);
            }
            if ((Flags & 0xFF0000) != 0xAA0000)
            {
                if (Bit(16)) bw.Write(Unk14);
                if (Bit(17)) bw.Write(Unk15);
                if (Bit(18)) bw.Write(Unk16);
                if (Bit(19)) bw.Write(Unk17);
                if (Bit(20)) bw.Write(UnkHash3);
                if (Bit(21)) bw.Write(Unk18);
                if (Bit(22)) bw.Write(Unk19);
                if (Bit(23)) bw.Write(Unk20);
            }
            if ((Flags & 0xFF000000) != 0xAA000000)
            {
                if (Bit(24)) bw.Write(Unk21);
                if (Bit(25)) bw.Write(UnkHash4);
                if (Bit(26)) bw.Write(UnkHash5);
                if (Bit(27)) bw.Write(Unk22);
                if (Bit(28)) bw.Write(Unk23);
                if (Bit(29)) bw.Write(Unk24);
                if (Bit(30)) bw.Write(Unk25); //maybe not
                if (Bit(31)) bw.Write(Unk26); //maybe not
            }

        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Flags", "0x" + Flags.Hex);

            if ((Flags & 0xFF) != 0xAA)
            {
                if (Bit(0)) RelXml.ValueTag(sb, indent, "Flags2", "0x" + Flags2.Hex);
                if (Bit(1)) RelXml.ValueTag(sb, indent, "Unk01", Unk01.ToString());
                if (Bit(2)) RelXml.ValueTag(sb, indent, "Unk02", Unk02.ToString());
                if (Bit(3)) RelXml.ValueTag(sb, indent, "Unk03", Unk03.ToString());
                if (Bit(4)) RelXml.ValueTag(sb, indent, "Unk04", Unk04.ToString());
                if (Bit(5)) RelXml.ValueTag(sb, indent, "Unk05", Unk05.ToString());
                if (Bit(6)) RelXml.ValueTag(sb, indent, "Unk06", Unk06.ToString());
                if (Bit(7)) RelXml.ValueTag(sb, indent, "Unk07", Unk07.ToString());
            }
            if ((Flags & 0xFF00) != 0xAA00)
            {
                if (Bit(8)) RelXml.ValueTag(sb, indent, "Unk08", Unk08.ToString());
                if (Bit(9)) RelXml.ValueTag(sb, indent, "Unk09", Unk09.ToString());
                if (Bit(10)) RelXml.ValueTag(sb, indent, "UnkInt1", UnkInt1.ToString());
                if (Bit(11)) RelXml.ValueTag(sb, indent, "UnkInt2", UnkInt2.ToString());
                if (Bit(12)) RelXml.ValueTag(sb, indent, "Unk10", Unk10.ToString());
                if (Bit(13)) RelXml.ValueTag(sb, indent, "Unk11", Unk11.ToString());
                if (Bit(14)) RelXml.ValueTag(sb, indent, "Unk12", Unk12.ToString());
                if (Bit(15)) RelXml.StringTag(sb, indent, "Category", RelXml.HashString(CategoryHash));
            }
            if ((Flags & 0xFF0000) != 0xAA0000)
            {
                if (Bit(16)) RelXml.ValueTag(sb, indent, "Unk14", Unk14.ToString());
                if (Bit(17)) RelXml.ValueTag(sb, indent, "Unk15", Unk15.ToString());
                if (Bit(18)) RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
                if (Bit(19)) RelXml.ValueTag(sb, indent, "Unk17", Unk17.ToString());
                if (Bit(20)) RelXml.StringTag(sb, indent, "UnkHash3", RelXml.HashString(UnkHash3));
                if (Bit(21)) RelXml.ValueTag(sb, indent, "Unk18", Unk18.ToString());
                if (Bit(22)) RelXml.ValueTag(sb, indent, "Unk19", Unk19.ToString());
                if (Bit(23)) RelXml.ValueTag(sb, indent, "Unk20", Unk20.ToString());
            }
            if ((Flags & 0xFF000000) != 0xAA000000)
            {
                if (Bit(24)) RelXml.ValueTag(sb, indent, "Unk21", Unk21.ToString());
                if (Bit(25)) RelXml.StringTag(sb, indent, "UnkHash4", RelXml.HashString(UnkHash4));
                if (Bit(26)) RelXml.StringTag(sb, indent, "UnkHash5", RelXml.HashString(UnkHash5));
                if (Bit(27)) RelXml.ValueTag(sb, indent, "Unk22", Unk22.ToString());
                if (Bit(28)) RelXml.ValueTag(sb, indent, "Unk23", Unk23.ToString());
                if (Bit(29)) RelXml.ValueTag(sb, indent, "Unk24", Unk24.ToString());
                if (Bit(30)) RelXml.ValueTag(sb, indent, "Unk25", Unk25.ToString()); //maybe not
                if (Bit(31)) RelXml.ValueTag(sb, indent, "Unk26", Unk26.ToString()); //maybe not
            }

        }
        public void ReadXml(XmlNode node)
        {
            Flags = Xml.GetChildUIntAttribute(node, "Flags", "value");

            if ((Flags & 0xFF) != 0xAA)
            {
                if (Bit(0)) Flags2 = Xml.GetChildUIntAttribute(node, "Flags2", "value");
                if (Bit(1)) Unk01 = (ushort)Xml.GetChildUIntAttribute(node, "Unk01", "value");
                if (Bit(2)) Unk02 = (ushort)Xml.GetChildUIntAttribute(node, "Unk02", "value");
                if (Bit(3)) Unk03 = (ushort)Xml.GetChildUIntAttribute(node, "Unk03", "value");
                if (Bit(4)) Unk04 = (ushort)Xml.GetChildUIntAttribute(node, "Unk04", "value");
                if (Bit(5)) Unk05 = (ushort)Xml.GetChildUIntAttribute(node, "Unk05", "value");
                if (Bit(6)) Unk06 = (ushort)Xml.GetChildUIntAttribute(node, "Unk06", "value");
                if (Bit(7)) Unk07 = (ushort)Xml.GetChildUIntAttribute(node, "Unk07", "value");
            }
            if ((Flags & 0xFF00) != 0xAA00)
            {
                if (Bit(8)) Unk08 = (ushort)Xml.GetChildUIntAttribute(node, "Unk08", "value");
                if (Bit(9)) Unk09 = (ushort)Xml.GetChildUIntAttribute(node, "Unk09", "value");
                if (Bit(10)) UnkInt1 = Xml.GetChildIntAttribute(node, "UnkInt1", "value");
                if (Bit(11)) UnkInt2 = Xml.GetChildIntAttribute(node, "UnkInt2", "value");
                if (Bit(12)) Unk10 = (ushort)Xml.GetChildUIntAttribute(node, "Unk10", "value");
                if (Bit(13)) Unk11 = (ushort)Xml.GetChildUIntAttribute(node, "Unk11", "value");
                if (Bit(14)) Unk12 = (ushort)Xml.GetChildUIntAttribute(node, "Unk12", "value");
                if (Bit(15)) CategoryHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "Category"));
            }
            if ((Flags & 0xFF0000) != 0xAA0000)
            {
                if (Bit(16)) Unk14 = (ushort)Xml.GetChildUIntAttribute(node, "Unk14", "value");
                if (Bit(17)) Unk15 = (ushort)Xml.GetChildUIntAttribute(node, "Unk15", "value");
                if (Bit(18)) Unk16 = (ushort)Xml.GetChildUIntAttribute(node, "Unk16", "value");
                if (Bit(19)) Unk17 = (ushort)Xml.GetChildUIntAttribute(node, "Unk17", "value");
                if (Bit(20)) UnkHash3 = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash3"));
                if (Bit(21)) Unk18 = (ushort)Xml.GetChildUIntAttribute(node, "Unk18", "value");
                if (Bit(22)) Unk19 = (byte)Xml.GetChildUIntAttribute(node, "Unk19", "value");
                if (Bit(23)) Unk20 = (byte)Xml.GetChildUIntAttribute(node, "Unk20", "value");
            }
            if ((Flags & 0xFF000000) != 0xAA000000)
            {
                if (Bit(24)) Unk21 = (byte)Xml.GetChildUIntAttribute(node, "Unk21", "value");
                if (Bit(25)) UnkHash4 = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash4"));
                if (Bit(26)) UnkHash5 = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash5"));
                if (Bit(27)) Unk22 = (ushort)Xml.GetChildUIntAttribute(node, "Unk22", "value");
                if (Bit(28)) Unk23 = (ushort)Xml.GetChildUIntAttribute(node, "Unk23", "value");
                if (Bit(29)) Unk24 = (ushort)Xml.GetChildUIntAttribute(node, "Unk24", "value");
                if (Bit(30)) Unk25 = (ushort)Xml.GetChildUIntAttribute(node, "Unk25", "value");
                if (Bit(31)) Unk26 = (ushort)Xml.GetChildUIntAttribute(node, "Unk26", "value");
            }

        }


        public uint CalcHeaderLength()
        {
            uint length = 4;
            if ((Flags & 0xFF) != 0xAA)
            {
                if (Bit(0)) length += 4;// Flags2 = br.ReadUInt32();
                if (Bit(1)) length += 2;// Unk01 = br.ReadUInt16();
                if (Bit(2)) length += 2;// Unk02 = br.ReadUInt16();
                if (Bit(3)) length += 2;// Unk03 = br.ReadUInt16();
                if (Bit(4)) length += 2;// Unk04 = br.ReadUInt16();
                if (Bit(5)) length += 2;// Unk05 = br.ReadUInt16();
                if (Bit(6)) length += 2;// Unk06 = br.ReadUInt16();
                if (Bit(7)) length += 2;// Unk07 = br.ReadUInt16();
            }
            if ((Flags & 0xFF00) != 0xAA00)
            {
                if (Bit(8)) length += 2;// Unk08 = br.ReadUInt16();
                if (Bit(9)) length += 2;// Unk09 = br.ReadUInt16();
                if (Bit(10)) length += 4;// UnkHash1 = br.ReadUInt32();
                if (Bit(11)) length += 4;// UnkHash2 = br.ReadUInt32();
                if (Bit(12)) length += 2;// Unk10 = br.ReadUInt16();
                if (Bit(13)) length += 2;// Unk11 = br.ReadUInt16();
                if (Bit(14)) length += 2;// Unk12 = br.ReadUInt16();
                if (Bit(15)) length += 4;// CategoryHash = br.ReadUInt32();
            }
            if ((Flags & 0xFF0000) != 0xAA0000)
            {
                if (Bit(16)) length += 2;// Unk14 = br.ReadUInt16();
                if (Bit(17)) length += 2;// Unk15 = br.ReadUInt16();
                if (Bit(18)) length += 2;// Unk16 = br.ReadUInt16();
                if (Bit(19)) length += 2;// Unk17 = br.ReadUInt16();
                if (Bit(20)) length += 4;// UnkHash3 = br.ReadUInt32();
                if (Bit(21)) length += 2;// Unk18 = br.ReadUInt16();
                if (Bit(22)) length += 1;// Unk19 = br.ReadByte();
                if (Bit(23)) length += 1;// Unk20 = br.ReadByte();
            }
            if ((Flags & 0xFF000000) != 0xAA000000)
            {
                if (Bit(24)) length += 1;// Unk21 = br.ReadByte();
                if (Bit(25)) length += 4;// UnkHash4 = br.ReadUInt32();
                if (Bit(26)) length += 4;// UnkHash5 = br.ReadUInt32();
                if (Bit(27)) length += 2;// Unk22 = br.ReadUInt16();
                if (Bit(28)) length += 2;// Unk23 = br.ReadUInt16();
                if (Bit(29)) length += 2;// Unk24 = br.ReadUInt16();
                if (Bit(30)) length += 2;// Unk25 = br.ReadUInt16(); //maybe not
                if (Bit(31)) length += 2;// Unk26 = br.ReadUInt16(); //maybe not
            }

            return length;
        }

        private bool Bit(int b)
        {
            return ((Flags & (1u << b)) != 0);
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}, {2}, {3}, {4}, {5}, {6}, {7}", Flags.Hex, Flags2.Hex, CategoryHash, UnkInt1, UnkInt2, UnkHash3, UnkHash4, UnkHash5);
        }
    }

    [TC(typeof(EXP))] public class RelSound : RelData
    {
        public RelSoundHeader Header { get; set; }
        public byte AudioTracksCount { get; set; }
        public RelData[] AudioTracks { get; set; }
        public MetaHash[] AudioTrackHashes { get; set; }
        public MetaHash[] AudioContainers { get; set; } //Relative path to parent wave container (i.e. "RESIDENT/animals")

        public RelSound(RelFile rel) : base(rel)
        {
        }
        public RelSound(RelData d, BinaryReader br) : base(d)
        {
            Header = new RelSoundHeader(br);
        }

        public void ReadAudioTrackHashes(BinaryReader br)
        {
            AudioTracksCount = br.ReadByte();
            AudioTrackHashes = new MetaHash[AudioTracksCount];
            for (int i = 0; i < AudioTracksCount; i++)
            {
                AudioTrackHashes[i] = br.ReadUInt32();
            }
        }
        public void WriteAudioTrackHashes(BinaryWriter bw)
        {
            bw.Write(AudioTracksCount);
            for (int i = 0; i < AudioTracksCount; i++)
            {
                bw.Write(AudioTrackHashes[i]);
            }
        }

        public override void Write(BinaryWriter bw)
        {
            bw.Write(TypeID);
            Header?.Write(bw);
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            WriteHeaderXml(sb, indent);
            base.WriteXml(sb, indent);//fallback case
        }

        public void WriteHeaderXml(StringBuilder sb, int indent)
        {
            if (Header == null) return;
            RelXml.OpenTag(sb, indent, "Header");// flags=\"0x" + Header.Flags.Hex + "\"");
            Header.WriteXml(sb, indent + 1);
            RelXml.CloseTag(sb, indent, "Header");
        }

        public void WriteAudioTracksXml(StringBuilder sb, int indent)
        {
            if (AudioTrackHashes == null) return;
            RelXml.OpenTag(sb, indent, "AudioTracks");
            var cind = indent + 1;
            foreach (var hash in AudioTrackHashes)
            {
                RelXml.StringTag(sb, cind, "Item", RelXml.HashString(hash));
            }
            RelXml.CloseTag(sb, indent, "AudioTracks");
        }


        public void ReadHeaderXml(XmlNode node)
        {
            var hnode = node.SelectSingleNode("Header");
            if (hnode == null) return;

            Header = new RelSoundHeader(hnode);
        }

        public void ReadAudioTracksXml(XmlNode node)
        {
            var atnode = node.SelectSingleNode("AudioTracks");
            if (atnode == null) return;

            var tracknodes = atnode.SelectNodes("Item");
            var tracklist = new List<MetaHash>();
            foreach (XmlNode tracknode in tracknodes)
            {
                tracklist.Add(XmlRel.GetHash(tracknode.InnerText));
            }
            AudioTrackHashes = tracklist.ToArray();
            AudioTracksCount = (byte)tracklist.Count;
        }

        public uint[] GetAudioTracksHashTableOffsets(uint offset = 0)
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioTracksCount; i++)
            {
                offsets.Add(offset + 1 + i * 4);
            }
            return offsets.ToArray();
        }

    }





    #region dat54


    public enum Dat54SoundType : byte
    {
        LoopingSound = 1,
        EnvelopeSound = 2,
        TwinLoopSound = 3,
        SpeechSound = 4,
        OnStopSound = 5,
        WrapperSound = 6,
        SequentialSound = 7,
        StreamingSound = 8,
        RetriggeredOverlappedSound = 9,
        CrossfadeSound = 10,
        CollapsingStereoSound = 11,
        SimpleSound = 12,
        MultitrackSound = 13,
        RandomizedSound = 14,
        EnvironmentSound = 15,
        DynamicEntitySound = 16,
        SequentialOverlapSound = 17,
        ModularSynthSound = 18,
        GranularSound = 19,
        DirectionalSound = 20,
        KineticSound = 21,
        SwitchSound = 22,
        VariableCurveSound = 23,
        VariablePrintValueSound = 24,
        VariableBlockSound = 25,
        IfSound = 26,
        MathOperationSound = 27,
        ParameterTransformSound = 28,
        FluctuatorSound = 29,
        AutomationSound = 30,
        ExternalStreamSound = 31,
        SoundSet = 32,
        Unknown = 33,
        Unknown2 = 34,
        SoundList = 35
    }

    [TC(typeof(EXP))] public class Dat54Sound : RelSound
    {
        public Dat54SoundType Type { get; set; }

        public Dat54Sound(RelFile rel, Dat54SoundType t) : base(rel)
        {
            Type = t;
            TypeID = (byte)t;
        }
        public Dat54Sound(RelData d, BinaryReader br) : base(d, br)
        {
            Type = (Dat54SoundType)TypeID;
        }

        public override void ReadXml(XmlNode node)
        {
            //don't use this as a fallback case! only for reading the header, for use with all defined Dat54Sounds!
            ReadHeaderXml(node);
            //base.ReadXml(node);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            //don't use this as a fallback case! only for writing the header, for use with all defined Dat54Sounds!
            WriteHeaderXml(sb, indent);
            //base.WriteXml(sb, indent);
        }

        public override string ToString()
        {
            return GetBaseString() + ": " + Type.ToString();
        }
    }

    [TC(typeof(EXP))] public class Dat54LoopingSound : Dat54Sound
    {
        public short UnkShort0 { get; set; } //0x0-0x2
        public short UnkShort1 { get; set; } //0x2-0x4
        public short UnkShort2 { get; set; } //0x4-0x6
        public MetaHash AudioHash { get; set; } //0x6-0xA
        public MetaHash ParameterHash { get; set; } //0xA-0xE

        public Dat54LoopingSound(RelFile rel) : base(rel, Dat54SoundType.LoopingSound)
        { }
        public Dat54LoopingSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkShort0 = br.ReadInt16();
            UnkShort1 = br.ReadInt16();
            UnkShort2 = br.ReadInt16();
            AudioHash = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash };
            ParameterHash = br.ReadUInt32();
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkShort0 = (short)Xml.GetChildIntAttribute(node, "UnkShort0", "value");
            UnkShort1 = (short)Xml.GetChildIntAttribute(node, "UnkShort1", "value");
            UnkShort2 = (short)Xml.GetChildIntAttribute(node, "UnkShort2", "value");
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            ParameterHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash"));
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkShort0", UnkShort0.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort1", UnkShort1.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort2", UnkShort2.ToString());
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            RelXml.StringTag(sb, indent, "ParameterHash", RelXml.HashString(ParameterHash));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkShort0);
            bw.Write(UnkShort1);
            bw.Write(UnkShort2);
            bw.Write(AudioHash);
            bw.Write(ParameterHash);
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 6 };
        }
    }
    [TC(typeof(EXP))] public class Dat54EnvelopeSound : Dat54Sound
    {
        public ushort UnkShort0 { get; set; } //0x0-0x2
        public ushort UnkShort1 { get; set; } //0x2-0x4
        public ushort UnkShort2 { get; set; } //0x4-0x6
        public ushort UnkShort3 { get; set; } //0x6-0x8
        public byte UnkByte0 { get; set; } //0x8-0x9
        public byte UnkByte1 { get; set; } //0x9-0xA
        public int UnkInt0 { get; set; } //0xA-0xE
        public ushort UnkShort4 { get; set; } //0xE-0x10
        public int UnkInt1 { get; set; } //0x10-0x14
        public int UnkInt2 { get; set; } //0x14-0x18
        public MetaHash CurvesUnkHash0 { get; set; } //0x18-0x1C
        public MetaHash CurvesUnkHash1 { get; set; } //0x1C-0x20
        public MetaHash CurvesUnkHash2 { get; set; } //0x20-0x24
        public MetaHash ParameterHash0 { get; set; } //0x24-0x28
        public MetaHash ParameterHash1 { get; set; } //0x28-0x2C
        public MetaHash ParameterHash2 { get; set; } //0x2C-0x30
        public MetaHash ParameterHash3 { get; set; } //0x30-0x34
        public MetaHash ParameterHash4 { get; set; } //0x34-0x38
        public MetaHash AudioHash { get; set; }// audio track 0x38-0x3C
        public int UnkInt3 { get; set; } //0x3C-0x40
        public MetaHash ParameterHash5 { get; set; } //0x40-0x44
        public float UnkFloat0 { get; set; } //0x44-0x48
        public float UnkFloat1 { get; set; } //0x48-0x4C

        public Dat54EnvelopeSound(RelFile rel) : base(rel, Dat54SoundType.EnvelopeSound)
        { }
        public Dat54EnvelopeSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkShort0 = br.ReadUInt16(); //0x0-0x2
            UnkShort1 = br.ReadUInt16(); //0x2-0x4
            UnkShort2 = br.ReadUInt16(); //0x4-0x6
            UnkShort3 = br.ReadUInt16(); //0x6-0x8
            UnkByte0 = br.ReadByte(); //0x8-0x9
            UnkByte1 = br.ReadByte(); //0x9-0xA
            UnkInt0 = br.ReadInt32(); //0xA-0xE
            UnkShort4 = br.ReadUInt16(); //0xE-0x10
            UnkInt1 = br.ReadInt32(); //0x10-0x14
            UnkInt2 = br.ReadInt32(); //0x14-0x18
            CurvesUnkHash0 = br.ReadUInt32(); //0x18-0x1C
            CurvesUnkHash1 = br.ReadUInt32(); //0x1C-0x20
            CurvesUnkHash2 = br.ReadUInt32(); //0x20-0x24
            ParameterHash0 = br.ReadUInt32(); //0x24-0x28
            ParameterHash1 = br.ReadUInt32(); //0x28-0x2C
            ParameterHash2 = br.ReadUInt32(); //0x2C-0x30
            ParameterHash3 = br.ReadUInt32(); //0x30-0x34
            ParameterHash4 = br.ReadUInt32(); //0x34-0x38
            AudioHash = br.ReadUInt32(); //0x38-0x3C
            UnkInt3 = br.ReadInt32(); //0x3C-0x40
            ParameterHash5 = br.ReadUInt32(); //0x40-0x44
            UnkFloat0 = br.ReadSingle(); //0x44-0x48
            UnkFloat1 = br.ReadSingle(); //0x48-0x4C
            AudioTrackHashes = new[] { AudioHash };
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkShort0 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort0", "value");
            UnkShort1 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort1", "value");
            UnkShort2 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort2", "value");
            UnkShort3 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort3", "value");
            UnkByte0 = (byte)Xml.GetChildUIntAttribute(node, "UnkByte0", "value");
            UnkByte1 = (byte)Xml.GetChildUIntAttribute(node, "UnkByte1", "value");
            UnkInt0 = Xml.GetChildIntAttribute(node, "UnkInt0", "value");
            UnkShort4 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort4", "value");
            UnkInt1 = Xml.GetChildIntAttribute(node, "UnkInt1", "value");
            UnkInt2 = Xml.GetChildIntAttribute(node, "UnkInt2", "value");
            CurvesUnkHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "CurvesUnkHash0"));
            CurvesUnkHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "CurvesUnkHash1"));
            CurvesUnkHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "CurvesUnkHash2"));
            ParameterHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash0"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));
            ParameterHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash2"));
            ParameterHash3 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash3"));
            ParameterHash4 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash4"));
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            UnkInt3 = Xml.GetChildIntAttribute(node, "UnkInt3", "value");
            ParameterHash5 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash5"));
            UnkFloat0 = Xml.GetChildFloatAttribute(node, "UnkFloat0", "value");
            UnkFloat1 = Xml.GetChildFloatAttribute(node, "UnkFloat1", "value");
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkShort0", UnkShort0.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort1", UnkShort1.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort2", UnkShort2.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort3", UnkShort3.ToString());
            RelXml.ValueTag(sb, indent, "UnkByte0", UnkByte0.ToString());
            RelXml.ValueTag(sb, indent, "UnkByte1", UnkByte1.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt0", UnkInt0.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort4", UnkShort4.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt1", UnkInt1.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt2", UnkInt2.ToString());
            RelXml.StringTag(sb, indent, "CurvesUnkHash0", RelXml.HashString(CurvesUnkHash0));
            RelXml.StringTag(sb, indent, "CurvesUnkHash1", RelXml.HashString(CurvesUnkHash1));
            RelXml.StringTag(sb, indent, "CurvesUnkHash2", RelXml.HashString(CurvesUnkHash2));
            RelXml.StringTag(sb, indent, "ParameterHash0", RelXml.HashString(ParameterHash0));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));
            RelXml.StringTag(sb, indent, "ParameterHash2", RelXml.HashString(ParameterHash2));
            RelXml.StringTag(sb, indent, "ParameterHash3", RelXml.HashString(ParameterHash3));
            RelXml.StringTag(sb, indent, "ParameterHash4", RelXml.HashString(ParameterHash4));
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            RelXml.ValueTag(sb, indent, "UnkInt3", UnkInt3.ToString());
            RelXml.StringTag(sb, indent, "ParameterHash5", RelXml.HashString(ParameterHash5));
            RelXml.ValueTag(sb, indent, "UnkFloat0", FloatUtil.ToString(UnkFloat0));
            RelXml.ValueTag(sb, indent, "UnkFloat1", FloatUtil.ToString(UnkFloat1));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkShort0); //0x0-0x2
            bw.Write(UnkShort1); //0x2-0x4
            bw.Write(UnkShort2); //0x4-0x6
            bw.Write(UnkShort3); //0x6-0x8
            bw.Write(UnkByte0); //0x8-0x9
            bw.Write(UnkByte1); //0x9-0xA
            bw.Write(UnkInt0); //0xA-0xE
            bw.Write(UnkShort4); //0xE-0x10
            bw.Write(UnkInt1); //0x10-0x14
            bw.Write(UnkInt2); //0x14-0x18
            bw.Write(CurvesUnkHash0); //0x18-0x1C
            bw.Write(CurvesUnkHash1); //0x1C-0x20
            bw.Write(CurvesUnkHash2); //0x20-0x24
            bw.Write(ParameterHash0); //0x24-0x28
            bw.Write(ParameterHash1); //0x28-0x2C
            bw.Write(ParameterHash2); //0x2C-0x30
            bw.Write(ParameterHash3); //0x30-0x34
            bw.Write(ParameterHash4); //0x34-0x38
            bw.Write(AudioHash); //0x38-0x3C
            bw.Write(UnkInt3); //0x3C-0x40
            bw.Write(ParameterHash5); //0x40-0x44
            bw.Write(UnkFloat0); //0x44-0x48
            bw.Write(UnkFloat1); //0x48-0x4C
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 56 };
        }
    }
    [TC(typeof(EXP))] public class Dat54TwinLoopSound : Dat54Sound
    {
        public ushort UnkShort0 { get; set; } //0x0-0x2
        public ushort UnkShort1 { get; set; } //0x2-0x4
        public ushort UnkShort2 { get; set; } //0x4-0x6
        public ushort UnkShort3 { get; set; } //0x6-0x8
        public MetaHash UnkHash { get; set; } //0x8-0xC
        public MetaHash ParameterHash0 { get; set; } //0xC-0x10
        public MetaHash ParameterHash1 { get; set; } //0x10-0x14
        public MetaHash ParameterHash2 { get; set; } //0x14-0x18
        public MetaHash ParameterHash3 { get; set; } //0x18-0x1C

        public Dat54TwinLoopSound(RelFile rel) : base(rel, Dat54SoundType.TwinLoopSound)
        { }
        public Dat54TwinLoopSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkShort0 = br.ReadUInt16();
            UnkShort1 = br.ReadUInt16();
            UnkShort2 = br.ReadUInt16();
            UnkShort3 = br.ReadUInt16();
            UnkHash = br.ReadUInt32();
            ParameterHash0 = br.ReadUInt32();
            ParameterHash1 = br.ReadUInt32();
            ParameterHash2 = br.ReadUInt32();
            ParameterHash3 = br.ReadUInt32();

            ReadAudioTrackHashes(br);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkShort0 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort0", "value");
            UnkShort1 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort1", "value");
            UnkShort2 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort2", "value");
            UnkShort3 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort3", "value");
            UnkHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash"));
            ParameterHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash0"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));
            ParameterHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash2"));
            ParameterHash3 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash3"));
            ReadAudioTracksXml(node);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkShort0", UnkShort0.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort1", UnkShort1.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort2", UnkShort2.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort3", UnkShort3.ToString());
            RelXml.StringTag(sb, indent, "UnkHash", RelXml.HashString(UnkHash));
            RelXml.StringTag(sb, indent, "ParameterHash0", RelXml.HashString(ParameterHash0));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));
            RelXml.StringTag(sb, indent, "ParameterHash2", RelXml.HashString(ParameterHash2));
            RelXml.StringTag(sb, indent, "ParameterHash3", RelXml.HashString(ParameterHash3));
            WriteAudioTracksXml(sb, indent);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkShort0);
            bw.Write(UnkShort1);
            bw.Write(UnkShort2);
            bw.Write(UnkShort3);
            bw.Write(UnkHash);
            bw.Write(ParameterHash0);
            bw.Write(ParameterHash1);
            bw.Write(ParameterHash2);
            bw.Write(ParameterHash3);

            WriteAudioTrackHashes(bw);
        }
        public override uint[] GetHashTableOffsets()
        {
            return GetAudioTracksHashTableOffsets(28);
        }
    }
    [TC(typeof(EXP))] public class Dat54SpeechSound : Dat54Sound
    {
        public int UnkInt0 { get; set; } //maybe file index?
        public int UnkInt1 { get; set; } //ox4-0x8
        public MetaHash VoiceDataHash { get; set; } //0x8-0xC
        public string SpeechName { get; set; } //0xD-...

        public Dat54SpeechSound(RelFile rel) : base(rel, Dat54SoundType.SpeechSound)
        { }
        public Dat54SpeechSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkInt0 = br.ReadInt32();
            UnkInt1 = br.ReadInt32();
            VoiceDataHash = br.ReadUInt32();
            SpeechName = br.ReadString();
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkInt0 = Xml.GetChildIntAttribute(node, "UnkInt0", "value");
            UnkInt1 = Xml.GetChildIntAttribute(node, "UnkInt1", "value");
            VoiceDataHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "VoiceDataHash"));
            SpeechName = Xml.GetChildInnerText(node, "SpeechName");
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkInt0", UnkInt0.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt1", UnkInt0.ToString());
            RelXml.StringTag(sb, indent, "VoiceDataHash", RelXml.HashString(VoiceDataHash));
            RelXml.StringTag(sb, indent, "SpeechName", SpeechName);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkInt0);
            bw.Write(UnkInt1);
            bw.Write(VoiceDataHash);
            bw.Write(SpeechName);
        }
    }
    [TC(typeof(EXP))] public class Dat54OnStopSound : Dat54Sound
    {
        public MetaHash AudioHash0 { get; set; }
        public MetaHash AudioHash1 { get; set; }
        public MetaHash AudioHash2 { get; set; }

        public Dat54OnStopSound(RelFile rel) : base(rel, Dat54SoundType.OnStopSound)
        { }
        public Dat54OnStopSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash0 = br.ReadUInt32();
            AudioHash1 = br.ReadUInt32();
            AudioHash2 = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash0, AudioHash1, AudioHash2 };
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash0"));
            AudioHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash1"));
            AudioHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash2"));
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash0", RelXml.HashString(AudioHash0));
            RelXml.StringTag(sb, indent, "AudioHash1", RelXml.HashString(AudioHash1));
            RelXml.StringTag(sb, indent, "AudioHash2", RelXml.HashString(AudioHash2));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash0);
            bw.Write(AudioHash1);
            bw.Write(AudioHash2);
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0, 4, 8 };
        }
    }
    [TC(typeof(EXP))] public class Dat54WrapperSound : Dat54Sound
    {
        public MetaHash AudioHash0 { get; set; } //0x0-0x4
        public int FrameStartTime { get; set; } //0x4-0x8  // maybe start delay?
        public MetaHash AudioHash1 { get; set; } //0x8-0xC
        public short FrameTimeInterval { get; set; } //0xC-0xE  // [camxx:] My guess is that this is related to the time at which a child sound should start playin (or the length of the sound).
        public byte ItemCount { get; set; }
        public MetaHash[] Variables { get; set; } //0xF
        public byte[] UnkByteData { get; set; } // ...

        public Dat54WrapperSound(RelFile rel) : base(rel, Dat54SoundType.WrapperSound)
        { }
        public Dat54WrapperSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash0 = br.ReadUInt32();
            FrameStartTime = br.ReadInt32();
            AudioHash1 = br.ReadUInt32();
            FrameTimeInterval = br.ReadInt16();
            ItemCount = br.ReadByte();
            Variables = new MetaHash[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                Variables[i] = br.ReadUInt32();
            }
            UnkByteData = br.ReadBytes(ItemCount);

            AudioTrackHashes = new[] { AudioHash0, AudioHash1 };
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash0"));
            FrameStartTime = Xml.GetChildIntAttribute(node, "FrameStartTime", "value");
            AudioHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash1"));
            FrameTimeInterval = (short)Xml.GetChildIntAttribute(node, "FrameTimeInterval", "value");
            var vnode = node.SelectSingleNode("Variables");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    var ulist = new List<byte>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(Xml.GetStringAttribute(inode, "key")));
                        ulist.Add((byte)Xml.GetIntAttribute(inode, "value"));
                    }
                    ItemCount = (byte)vlist.Count;
                    Variables = vlist.ToArray();
                    UnkByteData = ulist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash0", RelXml.HashString(AudioHash0));
            RelXml.ValueTag(sb, indent, "FrameStartTime", FrameStartTime.ToString());
            RelXml.StringTag(sb, indent, "AudioHash1", RelXml.HashString(AudioHash1));
            RelXml.ValueTag(sb, indent, "FrameTimeInterval", FrameTimeInterval.ToString());
            if (Variables?.Length > 0)
            {
                RelXml.OpenTag(sb, indent, "Variables");
                var cind = indent + 1;
                for (int i = 0; i < ItemCount; i++)
                {
                    var iname = RelXml.HashString(Variables[i]);
                    var ival = UnkByteData[i].ToString();
                    RelXml.SelfClosingTag(sb, cind, "Item key=\"" + iname + "\" value=\"" + ival + "\"");
                }
                RelXml.CloseTag(sb, indent, "Variables");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Variables");
            }
            WriteAudioTracksXml(sb, indent);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash0);
            bw.Write(FrameStartTime);
            bw.Write(AudioHash1);
            bw.Write(FrameTimeInterval);
            bw.Write(ItemCount);
            for (int i = 0; i < ItemCount; i++)
            {
                bw.Write(Variables[i]);
            }
            if (UnkByteData != null)
            {
                bw.Write(UnkByteData);
            }
            else
            { }
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0, 8 };
        }
    }
    [TC(typeof(EXP))] public class Dat54SequentialSound : Dat54Sound
    {
        public Dat54SequentialSound(RelFile rel) : base(rel, Dat54SoundType.SequentialSound)
        { }
        public Dat54SequentialSound(RelData d, BinaryReader br) : base(d, br)
        {
            ReadAudioTrackHashes(br);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            ReadAudioTracksXml(node);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            WriteAudioTracksXml(sb, indent);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            WriteAudioTrackHashes(bw);
        }
        public override uint[] GetHashTableOffsets()
        {
            return GetAudioTracksHashTableOffsets();
        }
    }
    [TC(typeof(EXP))] public class Dat54StreamingSound : Dat54Sound
    {
        int UnkInt { get; set; } //0x0-0x4

        public Dat54StreamingSound(RelFile rel) : base(rel, Dat54SoundType.StreamingSound)
        { }
        public Dat54StreamingSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkInt = br.ReadInt32();

            ReadAudioTrackHashes(br);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkInt = Xml.GetChildIntAttribute(node, "UnkInt", "value");
            ReadAudioTracksXml(node);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkInt", UnkInt.ToString());
            WriteAudioTracksXml(sb, indent);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkInt);
            WriteAudioTrackHashes(bw);
        }
        public override uint[] GetHashTableOffsets()
        {
            return GetAudioTracksHashTableOffsets(4);
        }
    }
    [TC(typeof(EXP))] public class Dat54RetriggeredOverlappedSound : Dat54Sound
    {
        public ushort UnkShort0 { get; set; } //0x0-0x2
        public ushort UnkShort1 { get; set; } //0x2-0x4
        public ushort UnkShort2 { get; set; } //0x4-0x6
        public ushort UnkShort3 { get; set; } // 0x6-0x8
        public MetaHash ParameterHash0 { get; set; } //0x8-0xC
        public MetaHash ParameterHash1 { get; set; } //0xC-0x10
        public MetaHash AudioHash0 { get; set; }
        public MetaHash AudioHash1 { get; set; }
        public MetaHash AudioHash2 { get; set; }

        public Dat54RetriggeredOverlappedSound(RelFile rel) : base(rel, Dat54SoundType.RetriggeredOverlappedSound)
        { }
        public Dat54RetriggeredOverlappedSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkShort0 = br.ReadUInt16();
            UnkShort1 = br.ReadUInt16();
            UnkShort2 = br.ReadUInt16();
            UnkShort3 = br.ReadUInt16();
            ParameterHash0 = br.ReadUInt32();
            ParameterHash1 = br.ReadUInt32();
            AudioHash0 = br.ReadUInt32();
            AudioHash1 = br.ReadUInt32();
            AudioHash2 = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash0, AudioHash1, AudioHash2 };
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkShort0 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort0", "value");
            UnkShort1 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort1", "value");
            UnkShort2 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort2", "value");
            UnkShort3 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort3", "value");
            ParameterHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash0"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));
            AudioHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash0"));
            AudioHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash1"));
            AudioHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash2"));
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkShort0", UnkShort0.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort1", UnkShort1.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort2", UnkShort2.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort3", UnkShort3.ToString());
            RelXml.StringTag(sb, indent, "ParameterHash0", RelXml.HashString(ParameterHash0));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));
            RelXml.StringTag(sb, indent, "AudioHash0", RelXml.HashString(AudioHash0));
            RelXml.StringTag(sb, indent, "AudioHash1", RelXml.HashString(AudioHash1));
            RelXml.StringTag(sb, indent, "AudioHash2", RelXml.HashString(AudioHash2));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkShort0);
            bw.Write(UnkShort1);
            bw.Write(UnkShort2);
            bw.Write(UnkShort3);
            bw.Write(ParameterHash0);
            bw.Write(ParameterHash1);
            bw.Write(AudioHash0);
            bw.Write(AudioHash1);
            bw.Write(AudioHash2);
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 16, 20, 24 };
        }
    }
    [TC(typeof(EXP))] public class Dat54CrossfadeSound : Dat54Sound
    {
        public MetaHash AudioHash0 { get; set; }
        public MetaHash AudioHash1 { get; set; }
        public byte UnkByte { get; set; } //0x8-0x9
        public float UnkFloat0 { get; set; } //0x9-0xD
        public float UnkFloat1 { get; set; } //0xD-0x11
        public int UnkInt { get; set; } //0xD-0x15
        public MetaHash UnkCurvesHash { get; set; } //0x15-0x19
        public MetaHash ParameterHash0 { get; set; } //0x19-0x1D
        public MetaHash ParameterHash1 { get; set; } //0x1D-0x21
        public MetaHash ParameterHash2 { get; set; } //0x21-0x25
        public MetaHash ParameterHash3 { get; set; } //0x25-0x29
        public MetaHash ParameterHash4 { get; set; } //0x29-0x2D

        public Dat54CrossfadeSound(RelFile rel) : base(rel, Dat54SoundType.CrossfadeSound)
        { }
        public Dat54CrossfadeSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash0 = br.ReadUInt32();
            AudioHash1 = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash0, AudioHash1 };
            UnkByte = br.ReadByte();
            UnkFloat0 = br.ReadSingle();
            UnkFloat1 = br.ReadSingle();
            UnkInt = br.ReadInt32();
            UnkCurvesHash = br.ReadUInt32();
            ParameterHash0 = br.ReadUInt32();
            ParameterHash1 = br.ReadUInt32();
            ParameterHash2 = br.ReadUInt32();
            ParameterHash3 = br.ReadUInt32();
            ParameterHash4 = br.ReadUInt32();
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash0"));
            AudioHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash1"));
            UnkByte = (byte)Xml.GetChildUIntAttribute(node, "UnkByte", "value");
            UnkFloat0 = Xml.GetChildFloatAttribute(node, "UnkFloat0", "value");
            UnkFloat1 = Xml.GetChildFloatAttribute(node, "UnkFloat1", "value");
            UnkInt = Xml.GetChildIntAttribute(node, "UnkInt", "value");
            UnkCurvesHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkCurvesHash"));
            ParameterHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash0"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));
            ParameterHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash2"));
            ParameterHash3 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash3"));
            ParameterHash4 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash4"));
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash0", RelXml.HashString(AudioHash0));
            RelXml.StringTag(sb, indent, "AudioHash1", RelXml.HashString(AudioHash1));
            RelXml.ValueTag(sb, indent, "UnkByte", UnkByte.ToString());
            RelXml.ValueTag(sb, indent, "UnkFloat0", FloatUtil.ToString(UnkFloat0));
            RelXml.ValueTag(sb, indent, "UnkFloat1", FloatUtil.ToString(UnkFloat1));
            RelXml.ValueTag(sb, indent, "UnkInt", UnkInt.ToString());
            RelXml.StringTag(sb, indent, "UnkCurvesHash", RelXml.HashString(UnkCurvesHash));
            RelXml.StringTag(sb, indent, "ParameterHash0", RelXml.HashString(ParameterHash0));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));
            RelXml.StringTag(sb, indent, "ParameterHash2", RelXml.HashString(ParameterHash2));
            RelXml.StringTag(sb, indent, "ParameterHash3", RelXml.HashString(ParameterHash3));
            RelXml.StringTag(sb, indent, "ParameterHash4", RelXml.HashString(ParameterHash4));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash0);
            bw.Write(AudioHash1);
            bw.Write(UnkByte);
            bw.Write(UnkFloat0);
            bw.Write(UnkFloat1);
            bw.Write(UnkInt);
            bw.Write(UnkCurvesHash);
            bw.Write(ParameterHash0);
            bw.Write(ParameterHash1);
            bw.Write(ParameterHash2);
            bw.Write(ParameterHash3);
            bw.Write(ParameterHash4);
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0, 4 };
        }
    }
    [TC(typeof(EXP))] public class Dat54CollapsingStereoSound : Dat54Sound
    {
        public MetaHash AudioHash0 { get; set; }
        public MetaHash AudioHash1 { get; set; }
        public float UnkFloat0 { get; set; }
        public float UnkFloat1 { get; set; }
        public MetaHash ParameterHash0 { get; set; } //0x10-0x14
        public MetaHash ParameterHash1 { get; set; } //0x14-0x18
        public MetaHash ParameterHash2 { get; set; } //0x18-0x1C
        public MetaHash ParameterHash3 { get; set; } //0x1C-0x20
        public MetaHash ParameterHash4 { get; set; } //0x20-0x24
        public MetaHash ParameterHash5 { get; set; } //0x28-0x2C
        public int UnkInt { get; set; } //0x24-0x28
        public byte UnkByte { get; set; } //0x2c-0x2D

        public Dat54CollapsingStereoSound(RelFile rel) : base(rel, Dat54SoundType.CollapsingStereoSound)
        { }
        public Dat54CollapsingStereoSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash0 = br.ReadUInt32();
            AudioHash1 = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash0, AudioHash1 };
            UnkFloat0 = br.ReadSingle(); //0x8
            UnkFloat1 = br.ReadSingle(); //0xC
            ParameterHash0 = br.ReadUInt32(); //0x10
            ParameterHash1 = br.ReadUInt32(); //0x14
            ParameterHash2 = br.ReadUInt32(); //0x18
            ParameterHash3 = br.ReadUInt32(); //0x1C
            ParameterHash4 = br.ReadUInt32(); //0x20
            UnkInt = br.ReadInt32(); //0x24-0x28
            ParameterHash5 = br.ReadUInt32(); //0x28-0x2C
            UnkByte = br.ReadByte(); //0x2C-0x2D
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash0"));
            AudioHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash1"));
            UnkFloat0 = Xml.GetChildFloatAttribute(node, "UnkFloat0", "value");
            UnkFloat1 = Xml.GetChildFloatAttribute(node, "UnkFloat1", "value");
            ParameterHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash0"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));
            ParameterHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash2"));
            ParameterHash3 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash3"));
            ParameterHash4 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash4"));
            ParameterHash5 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash5"));
            UnkInt = Xml.GetChildIntAttribute(node, "UnkInt", "value");
            UnkByte = (byte)Xml.GetChildUIntAttribute(node, "UnkByte", "value");
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash0", RelXml.HashString(AudioHash0));
            RelXml.StringTag(sb, indent, "AudioHash1", RelXml.HashString(AudioHash1));
            RelXml.ValueTag(sb, indent, "UnkFloat0", FloatUtil.ToString(UnkFloat0));
            RelXml.ValueTag(sb, indent, "UnkFloat1", FloatUtil.ToString(UnkFloat1));
            RelXml.StringTag(sb, indent, "ParameterHash0", RelXml.HashString(ParameterHash0));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));
            RelXml.StringTag(sb, indent, "ParameterHash2", RelXml.HashString(ParameterHash2));
            RelXml.StringTag(sb, indent, "ParameterHash3", RelXml.HashString(ParameterHash3));
            RelXml.StringTag(sb, indent, "ParameterHash4", RelXml.HashString(ParameterHash4));
            RelXml.ValueTag(sb, indent, "UnkInt", UnkInt.ToString());
            RelXml.StringTag(sb, indent, "ParameterHash5", RelXml.HashString(ParameterHash5));
            RelXml.ValueTag(sb, indent, "UnkByte", UnkByte.ToString());
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash0);
            bw.Write(AudioHash1);
            bw.Write(UnkFloat0); //0x8
            bw.Write(UnkFloat1); //0xC
            bw.Write(ParameterHash0); //0x10
            bw.Write(ParameterHash1); //0x14
            bw.Write(ParameterHash2); //0x18
            bw.Write(ParameterHash3); //0x1C
            bw.Write(ParameterHash4); //0x20
            bw.Write(UnkInt); //0x24-0x28
            bw.Write(ParameterHash5); //0x28-0x2C
            bw.Write(UnkByte); //0x2C-0x2D
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0, 4 };
        }
    }
    [TC(typeof(EXP))] public class Dat54SimpleSound : Dat54Sound
    {
        public MetaHash ContainerName { get; set; } //Relative path to parent wave container (i.e. "RESIDENT/animals")
        public MetaHash FileName { get; set; } //Name of the .wav file
        public byte WaveSlotNum { get; set; } //Internal index of wave (.awc) container

        public Dat54SimpleSound(RelFile rel) : base(rel, Dat54SoundType.SimpleSound)
        { }
        public Dat54SimpleSound(RelData d, BinaryReader br) : base(d, br)
        {
            ContainerName = br.ReadUInt32();
            AudioContainers = new[] { ContainerName };
            FileName = br.ReadUInt32();
            WaveSlotNum = br.ReadByte();
            if (br.BaseStream.Position < br.BaseStream.Length)
            { }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            ContainerName = XmlRel.GetHash(Xml.GetChildInnerText(node, "ContainerName"));
            FileName = XmlRel.GetHash(Xml.GetChildInnerText(node, "FileName"));
            WaveSlotNum = (byte)Xml.GetChildUIntAttribute(node, "WaveSlotNum", "value");
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "ContainerName", RelXml.HashString(ContainerName));
            RelXml.StringTag(sb, indent, "FileName", RelXml.HashString(FileName));
            RelXml.ValueTag(sb, indent, "WaveSlotNum", WaveSlotNum.ToString());
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(ContainerName);
            bw.Write(FileName);
            bw.Write(WaveSlotNum);
        }
        public override uint[] GetPackTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat54MultitrackSound : Dat54Sound
    {
        public Dat54MultitrackSound(RelFile rel) : base(rel, Dat54SoundType.MultitrackSound)
        { }
        public Dat54MultitrackSound(RelData d, BinaryReader br) : base(d, br)
        {
            ReadAudioTrackHashes(br);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            ReadAudioTracksXml(node);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            WriteAudioTracksXml(sb, indent);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            WriteAudioTrackHashes(bw);
        }
        public override uint[] GetHashTableOffsets()
        {
            return GetAudioTracksHashTableOffsets();
        }
    }
    [TC(typeof(EXP))] public class Dat54RandomizedSound : Dat54Sound
    {
        public byte UnkByte { get; set; } //0x0-0x1 something count?
        public byte UnkBytesCount { get; set; } //0x1-0x2
        public byte[] UnkBytes { get; set; }
        public byte ItemCount { get; set; }
        public float[] AudioTrackUnkFloats { get; set; } //probability..?

        public Dat54RandomizedSound(RelFile rel) : base(rel, Dat54SoundType.RandomizedSound)
        { }
        public Dat54RandomizedSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkByte = br.ReadByte();
            UnkBytesCount = br.ReadByte();
            UnkBytes = br.ReadBytes(UnkBytesCount);
            ItemCount = br.ReadByte();
            AudioTrackHashes = new MetaHash[ItemCount];
            AudioTrackUnkFloats = new float[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                AudioTrackHashes[i] = br.ReadUInt32();
                AudioTrackUnkFloats[i] = br.ReadSingle();
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkByte = (byte)Xml.GetChildUIntAttribute(node, "UnkByte", "value");
            UnkBytes = Xml.GetChildRawByteArray(node, "UnkBytes");
            UnkBytesCount = (byte)UnkBytes.Length;
            var vnode = node.SelectSingleNode("Items");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    var ulist = new List<float>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(Xml.GetStringAttribute(inode, "key")));
                        ulist.Add(Xml.GetFloatAttribute(inode, "value"));
                    }
                    ItemCount = (byte)vlist.Count;
                    AudioTrackHashes = vlist.ToArray();
                    AudioTrackUnkFloats = ulist.ToArray();
                }
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkByte", UnkByte.ToString());
            RelXml.WriteRawArray(sb, UnkBytes, indent, "UnkBytes", "", RelXml.FormatHexByte, 16);
            if (ItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                for (int i = 0; i < ItemCount; i++)
                {
                    var iname = RelXml.HashString(AudioTrackHashes[i]);
                    var ival = FloatUtil.ToString(AudioTrackUnkFloats[i]);
                    RelXml.SelfClosingTag(sb, cind, "Item key=\"" + iname + "\" value=\"" + ival + "\"");
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkByte);
            bw.Write(UnkBytesCount);
            bw.Write(UnkBytes);
            bw.Write(ItemCount);
            for (int i = 0; i < ItemCount; i++)
            {
                bw.Write(AudioTrackHashes[i]);
                bw.Write(AudioTrackUnkFloats[i]);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offset = 3u + UnkBytesCount;
            var offsets = new List<uint>();
            for (uint i = 0; i < ItemCount; i++)
            {
                offsets.Add(offset + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat54EnvironmentSound : Dat54Sound
    {
        public byte UnkByte { get; set; }

        public Dat54EnvironmentSound(RelFile rel) : base(rel, Dat54SoundType.EnvironmentSound)
        { }
        public Dat54EnvironmentSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkByte = br.ReadByte();
            if (br.BaseStream.Position < br.BaseStream.Length)
            { }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkByte = (byte)Xml.GetChildUIntAttribute(node, "UnkByte", "value");
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkByte", UnkByte.ToString());
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkByte);
        }
    }
    [TC(typeof(EXP))] public class Dat54DynamicEntitySound : Dat54Sound
    {
        public byte ItemCount { get; set; }
        public MetaHash[] UnkHashes { get; set; }

        public Dat54DynamicEntitySound(RelFile rel) : base(rel, Dat54SoundType.DynamicEntitySound)
        { }
        public Dat54DynamicEntitySound(RelData d, BinaryReader br) : base(d, br)
        {
            ItemCount = br.ReadByte();
            UnkHashes = new MetaHash[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                UnkHashes[i] = br.ReadUInt32();
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            var vnode = node.SelectSingleNode("Items");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    ItemCount = (byte)vlist.Count;
                    UnkHashes = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            if (ItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                for (int i = 0; i < ItemCount; i++)
                {
                    var iname = RelXml.HashString(UnkHashes[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(ItemCount);
            for (int i = 0; i < ItemCount; i++)
            {
                bw.Write(UnkHashes[i]);
            }
        }
    }
    [TC(typeof(EXP))] public class Dat54SequentialOverlapSound : Dat54Sound
    {
        public ushort UnkShort { get; set; }
        public MetaHash ParameterHash0 { get; set; } //0x2-0x6
        public MetaHash ParameterHash1 { get; set; } //0x6-0xA

        public Dat54SequentialOverlapSound(RelFile rel) : base(rel, Dat54SoundType.SequentialOverlapSound)
        { }
        public Dat54SequentialOverlapSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkShort = br.ReadUInt16();
            ParameterHash0 = br.ReadUInt32();
            ParameterHash1 = br.ReadUInt32();

            ReadAudioTrackHashes(br);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkShort = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort", "value");
            ParameterHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash0"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));

            ReadAudioTracksXml(node);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkShort", UnkShort.ToString());
            RelXml.StringTag(sb, indent, "ParameterHash0", RelXml.HashString(ParameterHash0));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));

            WriteAudioTracksXml(sb, indent);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkShort);
            bw.Write(ParameterHash0);
            bw.Write(ParameterHash1);
            WriteAudioTrackHashes(bw);
        }
        public override uint[] GetHashTableOffsets()
        {
            return GetAudioTracksHashTableOffsets(10);
        }
    }
    [TC(typeof(EXP))] public class Dat54ModularSynthSound : Dat54Sound
    {
        public MetaHash OptAmpUnkHash { get; set; } //0x0-0x4
        public MetaHash UnkHash { get; set; } //0x4-0x8
        public float UnkFloat { get; set; } //0x8-0xC
        public int UnkInt { get; set; } //0xC-0x10
        public int TrackCount { get; set; }
        public int UnkItemCount { get; set; }
        public Dat54ModularSynthSoundData[] UnkItems { get; set; } //0x28-..

        public Dat54ModularSynthSound(RelFile rel) : base(rel, Dat54SoundType.ModularSynthSound)
        { }
        public Dat54ModularSynthSound(RelData d, BinaryReader br) : base(d, br)
        {
            OptAmpUnkHash = br.ReadUInt32(); //0x0-0x4
            UnkHash = br.ReadUInt32(); //0x4-0x8
            UnkFloat = br.ReadSingle(); //0x8-0xC
            UnkInt = br.ReadInt32(); //0xC-0x10
            TrackCount = br.ReadInt32(); //0x10-0x14
            AudioTrackHashes = new MetaHash[4];
            for (int i = 0; i < 4; i++)
            {
                AudioTrackHashes[i] = br.ReadUInt32();
            }
            UnkItemCount = br.ReadInt32();
            UnkItems = new Dat54ModularSynthSoundData[UnkItemCount];
            for (int i = 0; i < UnkItemCount; i++)
            {
                UnkItems[i] = new Dat54ModularSynthSoundData(br);
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            OptAmpUnkHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "OptAmpUnkHash"));
            UnkHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash"));
            UnkFloat = Xml.GetChildFloatAttribute(node, "UnkFloat", "value");
            UnkInt = Xml.GetChildIntAttribute(node, "UnkInt", "value");
            TrackCount = Xml.GetChildIntAttribute(node, "TrackCount", "value");
            ReadAudioTracksXml(node);
            var vnode = node.SelectSingleNode("UnkItems");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54ModularSynthSoundData>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54ModularSynthSoundData();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    UnkItemCount = (byte)vlist.Count;
                    UnkItems = vlist.ToArray();
                }
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "OptAmpUnkHash", RelXml.HashString(OptAmpUnkHash));
            RelXml.StringTag(sb, indent, "UnkHash", RelXml.HashString(UnkHash));
            RelXml.ValueTag(sb, indent, "UnkFloat", FloatUtil.ToString(UnkFloat));
            RelXml.ValueTag(sb, indent, "UnkInt", UnkInt.ToString());
            RelXml.ValueTag(sb, indent, "TrackCount", TrackCount.ToString());
            WriteAudioTracksXml(sb, indent);
            if (UnkItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "UnkItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < UnkItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    UnkItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "UnkItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "UnkItems");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(OptAmpUnkHash); //0x0-0x4
            bw.Write(UnkHash); //0x4-0x8
            bw.Write(UnkFloat); //0x8-0xC
            bw.Write(UnkInt); //0xC-0x10
            bw.Write(TrackCount); //0x10-0x14
            for (int i = 0; i < 4; i++)
            {
                bw.Write(AudioTrackHashes[i]);
            }
            bw.Write(UnkItemCount);
            for (int i = 0; i < UnkItemCount; i++)
            {
                UnkItems[i].Write(bw);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < 4; i++)
            {
                offsets.Add(20 + i * 4);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat54ModularSynthSoundData
    {
        public MetaHash UnkHash { get; set; }
        public MetaHash ParameterHash { get; set; }
        public float Value { get; set; }

        public Dat54ModularSynthSoundData()
        { }
        public Dat54ModularSynthSoundData(BinaryReader br)
        {
            UnkHash = br.ReadUInt32();
            ParameterHash = br.ReadUInt32();
            Value = br.ReadSingle();
        }
        public void ReadXml(XmlNode node)
        {
            UnkHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash"));
            ParameterHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash"));
            Value = Xml.GetChildFloatAttribute(node, "Value", "value");
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.StringTag(sb, indent, "UnkHash", RelXml.HashString(UnkHash));
            RelXml.StringTag(sb, indent, "ParameterHash", RelXml.HashString(ParameterHash));
            RelXml.ValueTag(sb, indent, "Value", FloatUtil.ToString(Value));
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(UnkHash);
            bw.Write(ParameterHash);
            bw.Write(Value);
        }
        public override string ToString()
        {
            return UnkHash.ToString() + ": " + ParameterHash.ToString() + ": " + FloatUtil.ToString(Value);
        }
    }
    [TC(typeof(EXP))] public class Dat54GranularSound : Dat54Sound
    {
        public int WaveSlotIndex { get; set; } //0x0-0x4
        public Dat54GranularSoundFile Wave1 { get; set; }
        public Dat54GranularSoundFile Wave2 { get; set; }
        public Dat54GranularSoundFile Wave3 { get; set; }
        public Dat54GranularSoundFile Wave4 { get; set; }
        public Dat54GranularSoundFile Wave5 { get; set; }
        public Dat54GranularSoundFile Wave6 { get; set; }
        public Dat54GranularSoundData DataItem1 { get; set; } //0x34-0x3C
        public Dat54GranularSoundData DataItem2 { get; set; } //0x3C-0x44
        public Dat54GranularSoundData DataItem3 { get; set; } //0x44-0x4C
        public Dat54GranularSoundData DataItem4 { get; set; } //0x4C-0x54
        public Dat54GranularSoundData DataItem5 { get; set; } //0x54-0x5C
        public Dat54GranularSoundData DataItem6 { get; set; } //0x5C-0x64
        public int UnkInt0 { get; set; } //0x64-0x68
        public int UnkInt1 { get; set; } //0x68-0x6C
        public ushort UnkShort0 { get; set; } //0x6C-0x6E
        public ushort UnkShort1 { get; set; } //0x6E-0x70
        public ushort UnkShort2 { get; set; } //0x70-0x72
        public ushort UnkShort3 { get; set; } //0x72-0x74
        public ushort UnkShort4 { get; set; } //0x74-0x76
        public ushort UnkShort5 { get; set; } //0x76-0x78
        public MetaHash TrackName { get; set; } //0x78-0x7C
        public byte UnkVecCount { get; set; } //0x7C-0x7D
        public Vector2[] UnkVecData { get; set; } //0x7D-...

        public Dat54GranularSound(RelFile rel) : base(rel, Dat54SoundType.GranularSound)
        { }
        public Dat54GranularSound(RelData d, BinaryReader br) : base(d, br)
        {
            WaveSlotIndex = br.ReadInt32();

            Wave1 = new Dat54GranularSoundFile(br);
            Wave2 = new Dat54GranularSoundFile(br);
            Wave3 = new Dat54GranularSoundFile(br);
            Wave4 = new Dat54GranularSoundFile(br);
            Wave5 = new Dat54GranularSoundFile(br);
            Wave6 = new Dat54GranularSoundFile(br);

            AudioContainers = new[] {
                Wave1.ContainerName,
                Wave2.ContainerName,
                Wave3.ContainerName,
                Wave4.ContainerName,
                Wave5.ContainerName,
                Wave6.ContainerName
            };

            DataItem1 = new Dat54GranularSoundData(br);
            DataItem2 = new Dat54GranularSoundData(br);
            DataItem3 = new Dat54GranularSoundData(br);
            DataItem4 = new Dat54GranularSoundData(br);
            DataItem5 = new Dat54GranularSoundData(br);
            DataItem6 = new Dat54GranularSoundData(br);

            UnkInt0 = br.ReadInt32();
            UnkInt1 = br.ReadInt32();
            UnkShort0 = br.ReadUInt16();
            UnkShort1 = br.ReadUInt16();
            UnkShort2 = br.ReadUInt16();
            UnkShort3 = br.ReadUInt16();
            UnkShort4 = br.ReadUInt16();
            UnkShort5 = br.ReadUInt16();

            TrackName = br.ReadUInt32();

            AudioTrackHashes = new[] { TrackName };

            UnkVecCount = br.ReadByte();
            UnkVecData = new Vector2[UnkVecCount];
            for (int i = 0; i < UnkVecCount; i++)
            {
                UnkVecData[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            WaveSlotIndex = (byte)Xml.GetChildIntAttribute(node, "WaveSlotIndex", "value");
            Wave1 = new Dat54GranularSoundFile(node, "Wave1");
            Wave2 = new Dat54GranularSoundFile(node, "Wave2");
            Wave3 = new Dat54GranularSoundFile(node, "Wave3");
            Wave4 = new Dat54GranularSoundFile(node, "Wave4");
            Wave5 = new Dat54GranularSoundFile(node, "Wave5");
            Wave6 = new Dat54GranularSoundFile(node, "Wave6");
            DataItem1 = new Dat54GranularSoundData(node, "DataItem1");
            DataItem2 = new Dat54GranularSoundData(node, "DataItem2");
            DataItem3 = new Dat54GranularSoundData(node, "DataItem3");
            DataItem4 = new Dat54GranularSoundData(node, "DataItem4");
            DataItem5 = new Dat54GranularSoundData(node, "DataItem5");
            DataItem6 = new Dat54GranularSoundData(node, "DataItem6");
            UnkInt0 = Xml.GetChildIntAttribute(node, "UnkInt0", "value");
            UnkInt1 = Xml.GetChildIntAttribute(node, "UnkInt1", "value");
            UnkShort0 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort0", "value");
            UnkShort1 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort1", "value");
            UnkShort2 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort2", "value");
            UnkShort3 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort3", "value");
            UnkShort4 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort4", "value");
            UnkShort5 = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort5", "value");
            TrackName = XmlRel.GetHash(Xml.GetChildInnerText(node, "TrackName"));
            UnkVecData = Xml.GetChildRawVector2Array(node, "UnkVecData");
            UnkVecCount = (byte)UnkVecData?.Length;
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "WaveSlotIndex", WaveSlotIndex.ToString());
            Wave1.WriteXml(sb, indent, "Wave1");
            Wave2.WriteXml(sb, indent, "Wave2");
            Wave3.WriteXml(sb, indent, "Wave3");
            Wave4.WriteXml(sb, indent, "Wave4");
            Wave5.WriteXml(sb, indent, "Wave5");
            Wave6.WriteXml(sb, indent, "Wave6");
            DataItem1.WriteXml(sb, indent, "DataItem1");
            DataItem2.WriteXml(sb, indent, "DataItem2");
            DataItem3.WriteXml(sb, indent, "DataItem3");
            DataItem4.WriteXml(sb, indent, "DataItem4");
            DataItem5.WriteXml(sb, indent, "DataItem5");
            DataItem6.WriteXml(sb, indent, "DataItem6");
            RelXml.ValueTag(sb, indent, "UnkInt0", UnkInt0.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt1", UnkInt1.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort0", UnkShort0.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort1", UnkShort1.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort2", UnkShort2.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort3", UnkShort3.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort4", UnkShort4.ToString());
            RelXml.ValueTag(sb, indent, "UnkShort5", UnkShort5.ToString());
            RelXml.StringTag(sb, indent, "TrackName", RelXml.HashString(TrackName));
            RelXml.WriteRawArray(sb, UnkVecData, indent, "UnkVecData", "", RelXml.FormatVector2, 1);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);

            bw.Write(WaveSlotIndex);

            Wave1.Write(bw);
            Wave2.Write(bw);
            Wave3.Write(bw);
            Wave4.Write(bw);
            Wave5.Write(bw);
            Wave6.Write(bw);

            DataItem1.Write(bw);
            DataItem2.Write(bw);
            DataItem3.Write(bw);
            DataItem4.Write(bw);
            DataItem5.Write(bw);
            DataItem6.Write(bw);

            bw.Write(UnkInt0);
            bw.Write(UnkInt1);
            bw.Write(UnkShort0);
            bw.Write(UnkShort1);
            bw.Write(UnkShort2);
            bw.Write(UnkShort3);
            bw.Write(UnkShort4);
            bw.Write(UnkShort5);

            bw.Write(TrackName);

            bw.Write(UnkVecCount);
            for (int i = 0; i < UnkVecCount; i++)
            {
                bw.Write(UnkVecData[i].X);
                bw.Write(UnkVecData[i].Y);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 120 };
        }
        public override uint[] GetPackTableOffsets()
        {
            return new uint[] { 4, 12, 20, 28, 36, 44 };
        }
    }
    [TC(typeof(EXP))] public class Dat54GranularSoundFile
    {
        public MetaHash ContainerName { get; set; } //0x0-0x4
        public MetaHash FileName { get; set; } //0x4-0x8

        public Dat54GranularSoundFile(XmlNode node, string varName)
        {
            ReadXml(node, varName);
        }
        public Dat54GranularSoundFile(BinaryReader br)
        {
            ContainerName = br.ReadUInt32();
            FileName = br.ReadUInt32();
        }
        public void ReadXml(XmlNode node, string varName)
        {
            var cnode = node.SelectSingleNode(varName);
            ContainerName = XmlRel.GetHash(Xml.GetChildInnerText(cnode, "ContainerName"));
            FileName = XmlRel.GetHash(Xml.GetChildInnerText(cnode, "FileName"));
        }
        public void WriteXml(StringBuilder sb, int indent, string varName)
        {
            var cind = indent + 1;
            RelXml.OpenTag(sb, indent, varName);
            RelXml.StringTag(sb, cind, "ContainerName", RelXml.HashString(ContainerName));
            RelXml.StringTag(sb, cind, "FileName", RelXml.HashString(FileName));
            RelXml.CloseTag(sb, indent, varName);
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(ContainerName);
            bw.Write(FileName);
        }
        public override string ToString()
        {
            return ContainerName.ToString() + ": " + FileName.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54GranularSoundData
    {
        public byte UnkFlags0 { get; set; } //0x0-0x1
        public byte UnkFlags1 { get; set; } //0x1-0x2
        public byte UnkByte0 { get; set; } //0x2-0x3
        public byte UnkByte1 { get; set; } //0x3-0x4
        public float UnkFloat { get; set; } //0x4-0x8

        public Dat54GranularSoundData(XmlNode node, string varName)
        {
            ReadXml(node, varName);
        }
        public Dat54GranularSoundData(BinaryReader br)
        {
            UnkFlags0 = br.ReadByte();
            UnkFlags1 = br.ReadByte();
            UnkByte0 = br.ReadByte();
            UnkByte1 = br.ReadByte();
            UnkFloat = br.ReadSingle();
        }
        public void ReadXml(XmlNode node, string varName)
        {
            var cnode = node.SelectSingleNode(varName);
            UnkFlags0 = (byte)Xml.GetChildIntAttribute(cnode, "UnkFlags0", "value");
            UnkFlags1 = (byte)Xml.GetChildIntAttribute(cnode, "UnkFlags1", "value");
            UnkByte0 = (byte)Xml.GetChildIntAttribute(cnode, "UnkByte0", "value");
            UnkByte1 = (byte)Xml.GetChildIntAttribute(cnode, "UnkByte1", "value");
            UnkFloat = Xml.GetChildFloatAttribute(cnode, "UnkFloat", "value");
        }
        public void WriteXml(StringBuilder sb, int indent, string varName)
        {
            var cind = indent + 1;
            RelXml.OpenTag(sb, indent, varName);
            RelXml.ValueTag(sb, cind, "UnkFlags0", UnkFlags0.ToString());
            RelXml.ValueTag(sb, cind, "UnkFlags1", UnkFlags1.ToString());
            RelXml.ValueTag(sb, cind, "UnkByte0", UnkByte0.ToString());
            RelXml.ValueTag(sb, cind, "UnkByte1", UnkByte1.ToString());
            RelXml.ValueTag(sb, cind, "UnkFloat", FloatUtil.ToString(UnkFloat));
            RelXml.CloseTag(sb, indent, varName);
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(UnkFlags0);
            bw.Write(UnkFlags1);
            bw.Write(UnkByte0);
            bw.Write(UnkByte1);
            bw.Write(UnkFloat);
        }
        public override string ToString()
        {
            return UnkFlags0.ToString() + ": " + UnkFlags1.ToString() + ": " + UnkByte0.ToString() + ": " + UnkByte1.ToString() + ": " + FloatUtil.ToString(UnkFloat);
        }
    }
    [TC(typeof(EXP))] public class Dat54DirectionalSound : Dat54Sound
    {
        public MetaHash AudioHash { get; set; }
        public float UnkFloat0 { get; set; } //0x4-0x8
        public float UnkFloat1 { get; set; } //0x8-0xC
        public float UnkFloat2 { get; set; } //0xC-0x10
        public float UnkFloat3 { get; set; } //0x10-0x14
        public float UnkFloat4 { get; set; } //0x14-0x18

        public Dat54DirectionalSound(RelFile rel) : base(rel, Dat54SoundType.DirectionalSound)
        { }
        public Dat54DirectionalSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash };
            UnkFloat0 = br.ReadSingle();
            UnkFloat1 = br.ReadSingle();
            UnkFloat2 = br.ReadSingle();
            UnkFloat3 = br.ReadSingle();
            UnkFloat4 = br.ReadSingle();
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            UnkFloat0 = Xml.GetChildFloatAttribute(node, "UnkFloat0", "value");
            UnkFloat1 = Xml.GetChildFloatAttribute(node, "UnkFloat1", "value");
            UnkFloat2 = Xml.GetChildFloatAttribute(node, "UnkFloat2", "value");
            UnkFloat3 = Xml.GetChildFloatAttribute(node, "UnkFloat3", "value");
            UnkFloat4 = Xml.GetChildFloatAttribute(node, "UnkFloat4", "value");
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            RelXml.ValueTag(sb, indent, "UnkFloat0", FloatUtil.ToString(UnkFloat0));
            RelXml.ValueTag(sb, indent, "UnkFloat1", FloatUtil.ToString(UnkFloat1));
            RelXml.ValueTag(sb, indent, "UnkFloat2", FloatUtil.ToString(UnkFloat2));
            RelXml.ValueTag(sb, indent, "UnkFloat3", FloatUtil.ToString(UnkFloat3));
            RelXml.ValueTag(sb, indent, "UnkFloat4", FloatUtil.ToString(UnkFloat4));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash);
            bw.Write(UnkFloat0);
            bw.Write(UnkFloat1);
            bw.Write(UnkFloat2);
            bw.Write(UnkFloat3);
            bw.Write(UnkFloat4);
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat54KineticSound : Dat54Sound
    {
        public MetaHash AudioHash { get; set; }
        public float UnkFloat0 { get; set; } //Maybe kinetic force vector?
        public float UnkFloat1 { get; set; }
        public float UnkFloat2 { get; set; }

        public Dat54KineticSound(RelFile rel) : base(rel, Dat54SoundType.KineticSound)
        { }
        public Dat54KineticSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash };
            UnkFloat0 = br.ReadSingle();
            UnkFloat1 = br.ReadSingle();
            UnkFloat2 = br.ReadSingle();
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            UnkFloat0 = Xml.GetChildFloatAttribute(node, "UnkFloat0", "value");
            UnkFloat1 = Xml.GetChildFloatAttribute(node, "UnkFloat1", "value");
            UnkFloat2 = Xml.GetChildFloatAttribute(node, "UnkFloat2", "value");
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            RelXml.ValueTag(sb, indent, "UnkFloat0", FloatUtil.ToString(UnkFloat0));
            RelXml.ValueTag(sb, indent, "UnkFloat1", FloatUtil.ToString(UnkFloat1));
            RelXml.ValueTag(sb, indent, "UnkFloat2", FloatUtil.ToString(UnkFloat2));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash);
            bw.Write(UnkFloat0);
            bw.Write(UnkFloat1);
            bw.Write(UnkFloat2);
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat54SwitchSound : Dat54Sound
    {
        public MetaHash ParameterHash { get; set; } //0x0-0x4

        public Dat54SwitchSound(RelFile rel) : base(rel, Dat54SoundType.SwitchSound)
        { }
        public Dat54SwitchSound(RelData d, BinaryReader br) : base(d, br)
        {
            ParameterHash = br.ReadUInt32();

            ReadAudioTrackHashes(br);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            ParameterHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash"));
            ReadAudioTracksXml(node);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "ParameterHash", RelXml.HashString(ParameterHash));
            WriteAudioTracksXml(sb, indent);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(ParameterHash);
            WriteAudioTrackHashes(bw);
        }
        public override uint[] GetHashTableOffsets()
        {
            return GetAudioTracksHashTableOffsets(4);
        }
    }
    [TC(typeof(EXP))] public class Dat54VariableCurveSound : Dat54Sound
    {
        public MetaHash AudioHash { get; set; }
        public MetaHash ParameterHash0 { get; set; } //0x4-0x8
        public MetaHash ParameterHash1 { get; set; } //0x8-0xC
        public MetaHash UnkCurvesHash { get; set; } //0xC-0x10

        public Dat54VariableCurveSound(RelFile rel) : base(rel, Dat54SoundType.VariableCurveSound)
        { }
        public Dat54VariableCurveSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash };
            ParameterHash0 = br.ReadUInt32();
            ParameterHash1 = br.ReadUInt32();
            UnkCurvesHash = br.ReadUInt32();
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            ParameterHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash0"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));
            UnkCurvesHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkCurvesHash"));
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            RelXml.StringTag(sb, indent, "ParameterHash0", RelXml.HashString(ParameterHash0));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));
            RelXml.StringTag(sb, indent, "UnkCurvesHash", RelXml.HashString(UnkCurvesHash));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash);
            bw.Write(ParameterHash0);
            bw.Write(ParameterHash1);
            bw.Write(UnkCurvesHash);
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat54VariablePrintValueSound : Dat54Sound
    {
        public MetaHash ParameterHash { get; set; } //0x0-0x4
        public string VariableString { get; set; }

        public Dat54VariablePrintValueSound(RelFile rel) : base(rel, Dat54SoundType.VariablePrintValueSound)
        { }
        public Dat54VariablePrintValueSound(RelData d, BinaryReader br) : base(d, br)
        {
            ParameterHash = br.ReadUInt32();
            VariableString = br.ReadString();
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            ParameterHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash"));
            VariableString = Xml.GetChildInnerText(node, "VariableString");
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "ParameterHash", RelXml.HashString(ParameterHash));
            RelXml.StringTag(sb, indent, "VariableString", VariableString);
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(ParameterHash);
            bw.Write(VariableString);
        }
    }
    [TC(typeof(EXP))] public class Dat54VariableBlockSound : Dat54Sound
    {
        public MetaHash AudioHash { get; set; }
        public byte VariableCount { get; set; }
        public Dat54VariableData[] Variables { get; set; }

        public Dat54VariableBlockSound(RelFile rel) : base(rel, Dat54SoundType.VariableBlockSound)
        { }
        public Dat54VariableBlockSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash };
            VariableCount = br.ReadByte();
            Variables = new Dat54VariableData[VariableCount];
            for (int i = 0; i < VariableCount; i++)
            {
                Variables[i] = new Dat54VariableData(br);
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            var vnode = node.SelectSingleNode("Variables");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54VariableData>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54VariableData();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    VariableCount = (byte)vlist.Count;
                    Variables = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            if (VariableCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Variables");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < VariableCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Variables[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Variables");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Variables");
            }

        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash);
            bw.Write(VariableCount);
            for (int i = 0; i < VariableCount; i++)
            {
                Variables[i].Write(bw);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat54VariableData
    {
        public MetaHash Name { get; set; }
        public float Value { get; set; }
        public float UnkFloat { get; set; }
        public byte Flags { get; set; }

        public Dat54VariableData()
        { }
        public Dat54VariableData(BinaryReader br)
        {
            Name = br.ReadUInt32();
            Value = br.ReadSingle();
            UnkFloat = br.ReadSingle();
            Flags = br.ReadByte();
        }
        public void ReadXml(XmlNode node)
        {
            Name = XmlRel.GetHash(Xml.GetChildInnerText(node, "Name"));
            Value = Xml.GetChildFloatAttribute(node, "Value", "value");
            UnkFloat = Xml.GetChildFloatAttribute(node, "UnkFloat", "value");
            Flags = (byte)Xml.GetChildIntAttribute(node, "Flags", "value");
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.StringTag(sb, indent, "Name", RelXml.HashString(Name));
            RelXml.ValueTag(sb, indent, "Value", FloatUtil.ToString(Value));
            RelXml.ValueTag(sb, indent, "UnkFloat", FloatUtil.ToString(UnkFloat));
            RelXml.ValueTag(sb, indent, "Flags", Flags.ToString());
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(Name);
            bw.Write(Value);
            bw.Write(UnkFloat);
            bw.Write(Flags);
        }
        public override string ToString()
        {
            return Name + ": " + FloatUtil.ToString(Value) + ": " + FloatUtil.ToString(UnkFloat) + ": " + Flags.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54IfSound : Dat54Sound
    {
        public MetaHash AudioHash1 { get; set; }
        public MetaHash AudioHash2 { get; set; }
        public MetaHash ParameterHash1 { get; set; }
        public byte UnkByte { get; set; }
        public float UnkFloat { get; set; }
        public MetaHash ParameterHash2 { get; set; }

        public Dat54IfSound(RelFile rel) : base(rel, Dat54SoundType.IfSound)
        { }
        public Dat54IfSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash1 = br.ReadUInt32();
            AudioHash2 = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash1, AudioHash2 };
            ParameterHash1 = br.ReadUInt32();
            UnkByte = br.ReadByte();
            UnkFloat = br.ReadSingle();
            ParameterHash2 = br.ReadUInt32();
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash1"));
            AudioHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash2"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));
            UnkByte = (byte)Xml.GetChildIntAttribute(node, "UnkByte", "value");
            UnkFloat = Xml.GetChildFloatAttribute(node, "UnkFloat", "value");
            ParameterHash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash2"));
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash1", RelXml.HashString(AudioHash1));
            RelXml.StringTag(sb, indent, "AudioHash2", RelXml.HashString(AudioHash2));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));
            RelXml.ValueTag(sb, indent, "UnkByte", UnkByte.ToString());
            RelXml.ValueTag(sb, indent, "UnkFloat", FloatUtil.ToString(UnkFloat));
            RelXml.StringTag(sb, indent, "ParameterHash2", RelXml.HashString(ParameterHash2));
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash1);
            bw.Write(AudioHash2);
            bw.Write(ParameterHash1);
            bw.Write(UnkByte);
            bw.Write(UnkFloat);
            bw.Write(ParameterHash2);
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0, 4 };
        }
    }
    [TC(typeof(EXP))] public class Dat54MathOperationSound : Dat54Sound
    {
        public MetaHash AudioHash { get; set; }
        public byte UnkDataCount { get; set; }
        public Dat54MathOperationSoundData[] UnkData { get; set; }

        public Dat54MathOperationSound(RelFile rel) : base(rel, Dat54SoundType.MathOperationSound)
        { }
        public Dat54MathOperationSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash };
            UnkDataCount = br.ReadByte();
            UnkData = new Dat54MathOperationSoundData[UnkDataCount];
            for (int i = 0; i < UnkDataCount; i++)
            {
                UnkData[i] = new Dat54MathOperationSoundData(br);
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            var vnode = node.SelectSingleNode("UnkData");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54MathOperationSoundData>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54MathOperationSoundData();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    UnkDataCount = (byte)vlist.Count;
                    UnkData = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            if (UnkDataCount > 0)
            {
                RelXml.OpenTag(sb, indent, "UnkData");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < UnkDataCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    UnkData[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "UnkData");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "UnkData");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash);
            bw.Write(UnkDataCount);
            for (int i = 0; i < UnkDataCount; i++)
            {
                UnkData[i].Write(bw);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat54MathOperationSoundData
    {
        public byte UnkByte { get; set; } //0x0-0x1
        public int UnkInt0 { get; set; } //0x1-0x5
        public int UnkInt1 { get; set; } //0x5-0x9
        public int UnkInt2 { get; set; } //0x9-0xD
        public int UnkInt3 { get; set; } //0xD-0x11
        public int UnkInt4 { get; set; } //0x11-0x15
        public MetaHash ParameterHash0 { get; set; } //0x15-0x19
        public MetaHash ParameterHash1 { get; set; } //0x19-0x1D

        public Dat54MathOperationSoundData()
        { }
        public Dat54MathOperationSoundData(BinaryReader br)
        {
            UnkByte = br.ReadByte();
            UnkInt0 = br.ReadInt32();
            UnkInt1 = br.ReadInt32();
            UnkInt2 = br.ReadInt32();
            UnkInt3 = br.ReadInt32();
            UnkInt4 = br.ReadInt32();
            ParameterHash0 = br.ReadUInt32();
            ParameterHash1 = br.ReadUInt32();
        }
        public void ReadXml(XmlNode node)
        {
            UnkByte = (byte)Xml.GetChildIntAttribute(node, "UnkByte", "value");
            UnkInt0 = Xml.GetChildIntAttribute(node, "UnkInt0", "value");
            UnkInt1 = Xml.GetChildIntAttribute(node, "UnkInt1", "value");
            UnkInt2 = Xml.GetChildIntAttribute(node, "UnkInt2", "value");
            UnkInt3 = Xml.GetChildIntAttribute(node, "UnkInt3", "value");
            UnkInt4 = Xml.GetChildIntAttribute(node, "UnkInt4", "value");
            ParameterHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash0"));
            ParameterHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash1"));
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "UnkByte", UnkByte.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt0", UnkInt0.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt1", UnkInt1.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt2", UnkInt2.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt3", UnkInt3.ToString());
            RelXml.ValueTag(sb, indent, "UnkInt4", UnkInt4.ToString());
            RelXml.StringTag(sb, indent, "ParameterHash0", RelXml.HashString(ParameterHash0));
            RelXml.StringTag(sb, indent, "ParameterHash1", RelXml.HashString(ParameterHash1));
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(UnkByte);
            bw.Write(UnkInt0);
            bw.Write(UnkInt1);
            bw.Write(UnkInt2);
            bw.Write(UnkInt3);
            bw.Write(UnkInt4);
            bw.Write(ParameterHash0);
            bw.Write(ParameterHash1);
        }
        public override string ToString()
        {
            return ParameterHash0.ToString() + ", " + ParameterHash1.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54ParameterTransformSound : Dat54Sound
    {
        public MetaHash AudioHash { get; set; }
        public int ItemCount { get; set; }
        public Dat54ParameterTransformSoundData[] Items { get; set; }

        public Dat54ParameterTransformSound(RelFile rel) : base(rel, Dat54SoundType.ParameterTransformSound)
        { }
        public Dat54ParameterTransformSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash };
            ItemCount = br.ReadInt32(); //0x4-0x8
            Items = new Dat54ParameterTransformSoundData[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                Items[i] = new Dat54ParameterTransformSoundData(br);
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            var vnode = node.SelectSingleNode("Items");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54ParameterTransformSoundData>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54ParameterTransformSoundData();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ItemCount = vlist.Count;
                    Items = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            if (ItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < ItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Items[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash);
            bw.Write(ItemCount); //0x4-0x8
            for (int i = 0; i < ItemCount; i++)
            {
                Items[i].Write(bw);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat54ParameterTransformSoundData
    {
        public MetaHash ParameterHash { get; set; } //0x0-0x4
        public float UnkFloat0 { get; set; } //0x4-0x8
        public float UnkFloat1 { get; set; } //0x8-0xC
        public int NestedDataCount { get; set; }
        public Dat54ParameterTransformSoundData2[] NestedData { get; set; } //0x10..

        public Dat54ParameterTransformSoundData()
        { }
        public Dat54ParameterTransformSoundData(BinaryReader br)
        {
            ParameterHash = br.ReadUInt32();
            UnkFloat0 = br.ReadSingle();
            UnkFloat1 = br.ReadSingle();
            NestedDataCount = br.ReadInt32();
            NestedData = new Dat54ParameterTransformSoundData2[NestedDataCount];
            for (int i = 0; i < NestedDataCount; i++)
            {
                NestedData[i] = new Dat54ParameterTransformSoundData2(br);
            }
        }
        public void ReadXml(XmlNode node)
        {
            ParameterHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash"));
            UnkFloat0 = Xml.GetChildFloatAttribute(node, "UnkFloat0", "value");
            UnkFloat1 = Xml.GetChildFloatAttribute(node, "UnkFloat1", "value");
            var vnode = node.SelectSingleNode("NestedData");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54ParameterTransformSoundData2>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54ParameterTransformSoundData2();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    NestedDataCount = vlist.Count;
                    NestedData = vlist.ToArray();
                }
            }
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.StringTag(sb, indent, "ParameterHash", RelXml.HashString(ParameterHash));
            RelXml.ValueTag(sb, indent, "UnkFloat0", FloatUtil.ToString(UnkFloat0));
            RelXml.ValueTag(sb, indent, "UnkFloat1", FloatUtil.ToString(UnkFloat1));
            if (NestedDataCount > 0)
            {
                RelXml.OpenTag(sb, indent, "NestedData");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < NestedDataCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    NestedData[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "NestedData");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "NestedData");
            }
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(ParameterHash);
            bw.Write(UnkFloat0);
            bw.Write(UnkFloat1);
            bw.Write(NestedDataCount);
            for (int i = 0; i < NestedDataCount; i++)
            {
                NestedData[i].Write(bw);
            }
        }
        public override string ToString()
        {
            return ParameterHash.ToString() + ", " + NestedDataCount.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54ParameterTransformSoundData2
    {
        public float UnkFloat0 { get; set; } //0x0-0x4
        public int UnkInt { get; set; } //0x4
        public MetaHash ParameterHash { get; set; } //0x8-0xC
        public float UnkFloat1 { get; set; } //0xC
        public float UnkFloat2 { get; set; } //0x10-0x14
        public int NestedItemCount { get; set; }
        public Vector2[] NestedItems { get; set; } //0x18-...

        public Dat54ParameterTransformSoundData2()
        { }
        public Dat54ParameterTransformSoundData2(BinaryReader br)
        {
            UnkFloat0 = br.ReadSingle();
            UnkInt = br.ReadInt32();
            ParameterHash = br.ReadUInt32();
            UnkFloat1 = br.ReadSingle();
            UnkFloat2 = br.ReadSingle();
            NestedItemCount = br.ReadInt32();
            NestedItems = new Vector2[NestedItemCount];
            for (int i = 0; i < NestedItemCount; i++)
            {
                NestedItems[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            }
        }
        public void ReadXml(XmlNode node)
        {
            UnkFloat0 = Xml.GetChildFloatAttribute(node, "UnkFloat0", "value");
            UnkInt = Xml.GetChildIntAttribute(node, "UnkInt", "value");
            ParameterHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash"));
            UnkFloat1 = Xml.GetChildFloatAttribute(node, "UnkFloat1", "value");
            UnkFloat2 = Xml.GetChildFloatAttribute(node, "UnkFloat2", "value");
            NestedItems = Xml.GetChildRawVector2Array(node, "Vectors");
            NestedItemCount = NestedItems?.Length ?? 0;
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "UnkFloat0", FloatUtil.ToString(UnkFloat0));
            RelXml.ValueTag(sb, indent, "UnkInt", UnkInt.ToString());
            RelXml.StringTag(sb, indent, "ParameterHash", RelXml.HashString(ParameterHash));
            RelXml.ValueTag(sb, indent, "UnkFloat1", FloatUtil.ToString(UnkFloat1));
            RelXml.ValueTag(sb, indent, "UnkFloat2", FloatUtil.ToString(UnkFloat2));
            RelXml.WriteRawArray(sb, NestedItems, indent, "Vectors", "", RelXml.FormatVector2, 1);
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(UnkFloat0);
            bw.Write(UnkInt);
            bw.Write(ParameterHash);
            bw.Write(UnkFloat1);
            bw.Write(UnkFloat2);
            bw.Write(NestedItemCount);
            for (int i = 0; i < NestedItemCount; i++)
            {
                bw.Write(NestedItems[i].X);
                bw.Write(NestedItems[i].Y);
            }
        }
        public override string ToString()
        {
            return ParameterHash.ToString() + ", " + NestedItemCount.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54FluctuatorSound : Dat54Sound
    {
        public MetaHash AudioHash { get; set; }
        public int ItemCount { get; set; }
        public Dat54FluctuatorSoundData[] Items { get; set; }

        public Dat54FluctuatorSound(RelFile rel) : base(rel, Dat54SoundType.FluctuatorSound)
        { }
        public Dat54FluctuatorSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash };
            ItemCount = br.ReadInt32(); //0x4-0x8
            Items = new Dat54FluctuatorSoundData[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                Items[i] = new Dat54FluctuatorSoundData(br);
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash"));
            var vnode = node.SelectSingleNode("Items");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54FluctuatorSoundData>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54FluctuatorSoundData();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ItemCount = vlist.Count;
                    Items = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash", RelXml.HashString(AudioHash));
            if (ItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < ItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Items[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash);
            bw.Write(ItemCount); //0x4-0x8
            for (int i = 0; i < ItemCount; i++)
            {
                Items[i].Write(bw);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat54FluctuatorSoundData
    {
        public byte UnkByte0 { get; set; } //0x0-0x1
        public byte UnkByte1 { get; set; } //0x1-0x2
        public MetaHash ParameterHash { get; set; } //0x2-0x6
        public float UnkFloat00 { get; set; } //0x6-0xA
        public float UnkFloat01 { get; set; } //0xA-0xE
        public float UnkFloat02 { get; set; } //0xE-0x12
        public float UnkFloat03 { get; set; } //0x12-0x16
        public float UnkFloat04 { get; set; } //0x16-0x1A
        public float UnkFloat05 { get; set; } //0x1A-0x1E
        public float UnkFloat06 { get; set; } //0x1E-0x22
        public float UnkFloat07 { get; set; } //0x22-0x26
        public float UnkFloat08 { get; set; } //0x26-0x2A
        public float UnkFloat09 { get; set; } //0x2A-0x2E
        public float UnkFloat10 { get; set; } //0x2E-0x32

        public Dat54FluctuatorSoundData()
        { }
        public Dat54FluctuatorSoundData(BinaryReader br)
        {
            UnkByte0 = br.ReadByte();
            UnkByte1 = br.ReadByte();
            ParameterHash = br.ReadUInt32();
            UnkFloat00 = br.ReadSingle();
            UnkFloat01 = br.ReadSingle();
            UnkFloat02 = br.ReadSingle();
            UnkFloat03 = br.ReadSingle();
            UnkFloat04 = br.ReadSingle();
            UnkFloat05 = br.ReadSingle();
            UnkFloat06 = br.ReadSingle();
            UnkFloat07 = br.ReadSingle();
            UnkFloat08 = br.ReadSingle();
            UnkFloat09 = br.ReadSingle();
            UnkFloat10 = br.ReadSingle();
        }
        public void ReadXml(XmlNode node)
        {
            UnkByte0 = (byte)Xml.GetChildIntAttribute(node, "UnkByte0", "value");
            UnkByte1 = (byte)Xml.GetChildIntAttribute(node, "UnkByte1", "value");
            ParameterHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash"));
            UnkFloat00 = Xml.GetChildFloatAttribute(node, "UnkFloat00", "value");
            UnkFloat01 = Xml.GetChildFloatAttribute(node, "UnkFloat01", "value");
            UnkFloat02 = Xml.GetChildFloatAttribute(node, "UnkFloat02", "value");
            UnkFloat03 = Xml.GetChildFloatAttribute(node, "UnkFloat03", "value");
            UnkFloat04 = Xml.GetChildFloatAttribute(node, "UnkFloat04", "value");
            UnkFloat05 = Xml.GetChildFloatAttribute(node, "UnkFloat05", "value");
            UnkFloat06 = Xml.GetChildFloatAttribute(node, "UnkFloat06", "value");
            UnkFloat07 = Xml.GetChildFloatAttribute(node, "UnkFloat07", "value");
            UnkFloat08 = Xml.GetChildFloatAttribute(node, "UnkFloat08", "value");
            UnkFloat09 = Xml.GetChildFloatAttribute(node, "UnkFloat09", "value");
            UnkFloat10 = Xml.GetChildFloatAttribute(node, "UnkFloat10", "value");
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "UnkByte0", UnkByte0.ToString());
            RelXml.ValueTag(sb, indent, "UnkByte1", UnkByte1.ToString());
            RelXml.StringTag(sb, indent, "ParameterHash", RelXml.HashString(ParameterHash));
            RelXml.ValueTag(sb, indent, "UnkFloat00", FloatUtil.ToString(UnkFloat00));
            RelXml.ValueTag(sb, indent, "UnkFloat01", FloatUtil.ToString(UnkFloat01));
            RelXml.ValueTag(sb, indent, "UnkFloat02", FloatUtil.ToString(UnkFloat02));
            RelXml.ValueTag(sb, indent, "UnkFloat03", FloatUtil.ToString(UnkFloat03));
            RelXml.ValueTag(sb, indent, "UnkFloat04", FloatUtil.ToString(UnkFloat04));
            RelXml.ValueTag(sb, indent, "UnkFloat05", FloatUtil.ToString(UnkFloat05));
            RelXml.ValueTag(sb, indent, "UnkFloat06", FloatUtil.ToString(UnkFloat06));
            RelXml.ValueTag(sb, indent, "UnkFloat07", FloatUtil.ToString(UnkFloat07));
            RelXml.ValueTag(sb, indent, "UnkFloat08", FloatUtil.ToString(UnkFloat08));
            RelXml.ValueTag(sb, indent, "UnkFloat09", FloatUtil.ToString(UnkFloat09));
            RelXml.ValueTag(sb, indent, "UnkFloat10", FloatUtil.ToString(UnkFloat10));
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(UnkByte0);
            bw.Write(UnkByte1);
            bw.Write(ParameterHash);
            bw.Write(UnkFloat00);
            bw.Write(UnkFloat01);
            bw.Write(UnkFloat02);
            bw.Write(UnkFloat03);
            bw.Write(UnkFloat04);
            bw.Write(UnkFloat05);
            bw.Write(UnkFloat06);
            bw.Write(UnkFloat07);
            bw.Write(UnkFloat08);
            bw.Write(UnkFloat09);
            bw.Write(UnkFloat10);
        }
        public override string ToString()
        {
            return ParameterHash.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54AutomationSound : Dat54Sound
    {
        public MetaHash AudioHash0 { get; set; }
        public float UnkFloat0 { get; set; } //0x4-0x8
        public float UnkFloat1 { get; set; } //0x8-0xC
        public MetaHash ParameterHash { get; set; } //0xC-0x10
        public MetaHash AudioHash1 { get; set; }
        public MetaHash WaveSlotId { get; set; } //0x14-0x18
        public MetaHash UnkHash1 { get; set; } //0x18-0x1C //pack hash?
        public int UnkDataCount { get; set; } // array data count 0x1C-0x20
        public Dat54AutomationSoundData[] UnkData { get; set; } //0x20-

        public Dat54AutomationSound(RelFile rel) : base(rel, Dat54SoundType.AutomationSound)
        { }
        public Dat54AutomationSound(RelData d, BinaryReader br) : base(d, br)
        {
            AudioHash0 = br.ReadUInt32();
            UnkFloat0 = br.ReadSingle();
            UnkFloat1 = br.ReadSingle();
            ParameterHash = br.ReadUInt32();
            AudioHash1 = br.ReadUInt32();
            AudioTrackHashes = new[] { AudioHash0, AudioHash1 };
            WaveSlotId = br.ReadUInt32();
            UnkHash1 = br.ReadUInt32();
            UnkDataCount = br.ReadInt32();
            UnkData = new Dat54AutomationSoundData[UnkDataCount];
            for (int i = 0; i < UnkDataCount; i++)
            {
                UnkData[i] = new Dat54AutomationSoundData(br);
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AudioHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash0"));
            UnkFloat0 = Xml.GetChildFloatAttribute(node, "UnkFloat0", "value");
            UnkFloat1 = Xml.GetChildFloatAttribute(node, "UnkFloat1", "value");
            ParameterHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "ParameterHash"));
            AudioHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioHash1"));
            WaveSlotId = XmlRel.GetHash(Xml.GetChildInnerText(node, "WaveSlotId"));
            UnkHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash1"));
            var vnode = node.SelectSingleNode("UnkData");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54AutomationSoundData>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54AutomationSoundData();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    UnkDataCount = vlist.Count;
                    UnkData = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.StringTag(sb, indent, "AudioHash0", RelXml.HashString(AudioHash0));
            RelXml.ValueTag(sb, indent, "UnkFloat0", FloatUtil.ToString(UnkFloat0));
            RelXml.ValueTag(sb, indent, "UnkFloat1", FloatUtil.ToString(UnkFloat1));
            RelXml.StringTag(sb, indent, "ParameterHash", RelXml.HashString(ParameterHash));
            RelXml.StringTag(sb, indent, "AudioHash1", RelXml.HashString(AudioHash1));
            RelXml.StringTag(sb, indent, "WaveSlotId", RelXml.HashString(WaveSlotId));
            RelXml.StringTag(sb, indent, "UnkHash1", RelXml.HashString(UnkHash1));
            if (UnkDataCount > 0)
            {
                RelXml.OpenTag(sb, indent, "UnkData");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < UnkDataCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    UnkData[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "UnkData");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "UnkData");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(AudioHash0);
            bw.Write(UnkFloat0);
            bw.Write(UnkFloat1);
            bw.Write(ParameterHash);
            bw.Write(AudioHash1);
            bw.Write(WaveSlotId);
            bw.Write(UnkHash1);
            bw.Write(UnkDataCount);
            for (int i = 0; i < UnkDataCount; i++)
            {
                UnkData[i].Write(bw);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0, 16 };
        }
        public override uint[] GetPackTableOffsets()
        {
            return new uint[] { 20 };
        }
    }
    [TC(typeof(EXP))] public class Dat54AutomationSoundData
    {
        public int UnkInt { get; set; } //0x0-0x1
        public MetaHash UnkHash { get; set; } //0x2-0x6

        public Dat54AutomationSoundData()
        { }
        public Dat54AutomationSoundData(BinaryReader br)
        {
            UnkInt = br.ReadInt32();
            UnkHash = br.ReadUInt32();

            if (UnkInt != 0)//should be pack hash?
            { }
        }
        public void ReadXml(XmlNode node)
        {
            UnkInt = Xml.GetChildIntAttribute(node, "UnkInt", "value");
            UnkHash = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash"));
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "UnkInt", UnkInt.ToString());
            RelXml.StringTag(sb, indent, "UnkHash", RelXml.HashString(UnkHash));
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(UnkInt);
            bw.Write(UnkHash);
        }
        public override string ToString()
        {
            return UnkInt.ToString() + ", " + UnkHash.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54ExternalStreamSound : Dat54Sound
    {
        public MetaHash Unk0 { get; set; }
        public MetaHash Unk1 { get; set; }
        public MetaHash Unk2 { get; set; }
        public MetaHash Unk3 { get; set; }

        public Dat54ExternalStreamSound(RelFile rel) : base(rel, Dat54SoundType.ExternalStreamSound)
        { }
        public Dat54ExternalStreamSound(RelData d, BinaryReader br) : base(d, br)
        {
            ReadAudioTrackHashes(br);

            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadUInt32();

            if (AudioTracksCount == 0)
            {
                Unk2 = br.ReadUInt32();
                Unk3 = br.ReadUInt32();
            }

            if (br.BaseStream.Position != br.BaseStream.Length)
            {

                //var bytes = new List<byte>();
                //while (br.BaseStream.Position < br.BaseStream.Length)
                //{
                //    byte b = br.ReadByte();
                //    bytes.Add(b);
                //    if (b != 0)
                //    { }//no hits here
                //}
                ////var bytearr = bytes.ToArray();

            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            ReadAudioTracksXml(node);
            Unk0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk0"));
            Unk1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk1"));
            if (AudioTracksCount == 0)
            {
                Unk2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk2"));
                Unk3 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk3"));
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            WriteAudioTracksXml(sb, indent);
            RelXml.StringTag(sb, indent, "Unk0", RelXml.HashString(Unk0));
            RelXml.StringTag(sb, indent, "Unk1", RelXml.HashString(Unk1));
            if (AudioTracksCount == 0)
            {
                RelXml.StringTag(sb, indent, "Unk2", RelXml.HashString(Unk2));
                RelXml.StringTag(sb, indent, "Unk3", RelXml.HashString(Unk3));
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            WriteAudioTrackHashes(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);

            if (AudioTracksCount == 0)
            {
                bw.Write(Unk2);
                bw.Write(Unk3);
            }

        }
        public override uint[] GetHashTableOffsets()
        {
            var list = GetAudioTracksHashTableOffsets().ToList();
            uint offs = (uint)list.Count * 4 + 1;
            list.Add(offs);// Unk0
            list.Add(offs + 4);// Unk1
            if (AudioTracksCount == 0)
            {
                list.Add(offs + 8);// Unk2
                list.Add(offs + 12);// Unk3
            }
            return list.ToArray();
            //return GetAudioTracksHashTableOffsets();
        }
    }
    [TC(typeof(EXP))] public class Dat54SoundSet : Dat54Sound
    {
        public int ItemCount { get; set; }
        public Dat54SoundSetItem[] Items { get; set; }

        public Dat54SoundSet(RelFile rel) : base(rel, Dat54SoundType.SoundSet)
        { }
        public Dat54SoundSet(RelData d, BinaryReader br) : base(d, br)
        {
            ItemCount = br.ReadInt32();
            Items = new Dat54SoundSetItem[ItemCount];
            AudioTrackHashes = new MetaHash[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                Items[i] = new Dat54SoundSetItem(br);
                AudioTrackHashes[i] = Items[i].SoundName;
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            var vnode = node.SelectSingleNode("Items");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54SoundSetItem>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54SoundSetItem();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ItemCount = vlist.Count;
                    Items = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            if (ItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < ItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Items[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(ItemCount);
            for (int i = 0; i < ItemCount; i++)
            {
                Items[i].Write(bw);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < ItemCount; i++)
            {
                offsets.Add(8 + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat54SoundSetItem
    {
        public MetaHash ScriptName { get; set; }
        public MetaHash SoundName { get; set; }

        public Dat54SoundSetItem()
        { }
        public Dat54SoundSetItem(BinaryReader br)
        {
            ScriptName = br.ReadUInt32();
            SoundName = br.ReadUInt32();
        }
        public void ReadXml(XmlNode node)
        {
            ScriptName = XmlRel.GetHash(Xml.GetChildInnerText(node, "ScriptName"));
            SoundName = XmlRel.GetHash(Xml.GetChildInnerText(node, "SoundName"));
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.StringTag(sb, indent, "ScriptName", RelXml.HashString(ScriptName));
            RelXml.StringTag(sb, indent, "SoundName", RelXml.HashString(SoundName));
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(ScriptName);
            bw.Write(SoundName);
        }
        public override string ToString()
        {
            return ScriptName.ToString() + ": " + SoundName.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54UnknownSound : Dat54Sound
    {
        public byte UnkDataCount { get; set; }
        public Dat54UnknownSoundData[] UnkData { get; set; }

        public Dat54UnknownSound(RelFile rel) : base(rel, Dat54SoundType.Unknown)
        { }
        public Dat54UnknownSound(RelData d, BinaryReader br) : base(d, br)
        {
            UnkDataCount = br.ReadByte();
            UnkData = new Dat54UnknownSoundData[UnkDataCount];
            AudioTrackHashes = new MetaHash[UnkDataCount];
            for (int i = 0; i < UnkDataCount; i++)
            {
                UnkData[i] = new Dat54UnknownSoundData(br);
                AudioTrackHashes[i] = UnkData[i].AudioTrack;// br.ReadUInt32();
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            var vnode = node.SelectSingleNode("UnkData");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat54UnknownSoundData>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat54UnknownSoundData();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    UnkDataCount = (byte)vlist.Count;
                    UnkData = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            if (UnkDataCount > 0)
            {
                RelXml.OpenTag(sb, indent, "UnkData");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < UnkDataCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    UnkData[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "UnkData");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "UnkData");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkDataCount);
            for (int i = 0; i < UnkDataCount; i++)
            {
                UnkData[i].Write(bw);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < UnkDataCount; i++)
            {
                offsets.Add(4 + i * 7);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat54UnknownSoundData
    {
        public byte UnkByte0 { get; set; }
        public byte UnkByte1 { get; set; }
        public byte UnkByte2 { get; set; }
        public MetaHash AudioTrack { get; set; }

        public Dat54UnknownSoundData()
        { }
        public Dat54UnknownSoundData(BinaryReader br)
        {
            UnkByte0 = br.ReadByte();
            UnkByte1 = br.ReadByte();
            UnkByte2 = br.ReadByte();
            AudioTrack = br.ReadUInt32();
        }
        public void ReadXml(XmlNode node)
        {
            UnkByte0 = (byte)Xml.GetChildIntAttribute(node, "UnkByte0", "value");
            UnkByte1 = (byte)Xml.GetChildIntAttribute(node, "UnkByte1", "value");
            UnkByte2 = (byte)Xml.GetChildIntAttribute(node, "UnkByte2", "value");
            AudioTrack = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack"));
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "UnkByte0", UnkByte0.ToString());
            RelXml.ValueTag(sb, indent, "UnkByte1", UnkByte1.ToString());
            RelXml.ValueTag(sb, indent, "UnkByte2", UnkByte2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack", RelXml.HashString(AudioTrack));
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(UnkByte0);
            bw.Write(UnkByte1);
            bw.Write(UnkByte2);
            bw.Write(AudioTrack);
        }
        public override string ToString()
        {
            return UnkByte0.ToString() + ": " + UnkByte1.ToString() + ": " + UnkByte2.ToString();
        }
    }
    [TC(typeof(EXP))] public class Dat54UnknownSound2 : Dat54Sound
    {
        public uint UnkCount { get; set; }
        public MetaHash[] UnkItems { get; set; }

        public Dat54UnknownSound2(RelFile rel) : base(rel, Dat54SoundType.Unknown2)
        { }
        public Dat54UnknownSound2(RelData d, BinaryReader br) : base(d, br)
        {
            UnkCount = br.ReadUInt32();
            UnkItems = new MetaHash[UnkCount];
            for (int i = 0; i < UnkCount; i++)
            {
                UnkItems[i] = br.ReadUInt32();
            }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            var vnode = node.SelectSingleNode("Items");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    UnkCount = (uint)vlist.Count;
                    UnkItems = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            if (UnkCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                for (int i = 0; i < UnkCount; i++)
                {
                    var iname = RelXml.HashString(UnkItems[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkCount);
            for (int i = 0; i < UnkCount; i++)
            {
                bw.Write(UnkItems[i]);
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < UnkCount; i++)
            {
                offsets.Add(4 + i * 4);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat54SoundList : Dat54Sound
    {
        public ushort UnkShort { get; set; }
        public uint Count { get; set; }
        public MetaHash[] Items { get; set; }

        public Dat54SoundList(RelFile rel) : base(rel, Dat54SoundType.SoundList)
        { }
        public Dat54SoundList(RelData d, BinaryReader br) : base(d, br)
        {
            UnkShort = br.ReadUInt16();
            Count = br.ReadUInt32();
            Items = new MetaHash[Count];
            for (int i = 0; i < Count; i++)
            {
                Items[i] = br.ReadUInt32();
            }
            if (br.BaseStream.Position != br.BaseStream.Length)
            { }
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            UnkShort = (ushort)Xml.GetChildUIntAttribute(node, "UnkShort", "value");
            var vnode = node.SelectSingleNode("Items");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    Count = (uint)vlist.Count;
                    Items = vlist.ToArray();
                }
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            RelXml.ValueTag(sb, indent, "UnkShort", UnkShort.ToString());
            if (Count > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                for (int i = 0; i < Count; i++)
                {
                    var iname = RelXml.HashString(Items[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void Write(BinaryWriter bw)
        {
            base.Write(bw);
            bw.Write(UnkShort);
            bw.Write(Count);
            for (int i = 0; i < Count; i++)
            {
                bw.Write(Items[i]);
            }
        }
    }


    #endregion




    #region dat151


    public enum Dat151RelType : byte //not sure how correct these are?
    {
        Collision = 1, //maybe for vehicle
        Unk2 = 2,
        Vehicle = 3,
        VehicleEngine = 4,
        Entity = 5, //not sure about this
        StaticEmitter = 6,//probably
        Unk7 = 7,
        Helicopter = 8, //maybe
        Unk9 = 9,
        Unk11 = 11,
        Unk12 = 12,
        Unk13 = 13,
        SpeechParams = 14,
        Unk15 = 15,
        Unk16 = 16,
        Weapon = 17,
        Unk18 = 18,
        Unk22 = 22,
        Unk23 = 23,
        RadioStations = 24, //
        RadioStation = 25, 
        RadioMusic = 26,
        Unk27 = 27,
        Unk28 = 28,
        Unk29 = 29,
        PedPVG = 30, //maybe Ped Voice Group?
        Unk31 = 31,
        AmbientEmitterList = 32,
        Unk33 = 33,
        Unk35 = 35,
        Unk36 = 36,
        AmbientZone = 37,
        AmbientEmitter = 38,
        AmbientZoneList = 39,
        Unk40 = 40,
        Unk41 = 41,
        Unk42 = 42,
        Interior = 44,
        Unk45 = 45,
        InteriorRoom = 46,
        Unk47 = 47,
        Unk48 = 48,
        Unk49 = 49, //doors/gates?
        WeaponAudioItem = 50,
        Unk51 = 51,
        Mod = 52, //what actually is a "mod" here? a change in some audio settings maybe?
        Unk53 = 53,
        Unk54 = 54,
        Unk55 = 55,
        Unk56 = 56,
        Aeroplane = 57,
        Unk59 = 59,
        Mood = 62,
        StartTrackAction = 63,
        StopTrackAction = 64,
        SetMoodAction = 65,
        PlayerAction = 66,
        StartOneShotAction = 67,
        StopOneShotAction = 68,
        Unk69 = 69,
        Unk70 = 70,
        Unk71 = 71,
        Unk72 = 72,
        AnimalParams = 73,
        Unk74 = 74,
        Unk75 = 75,
        VehicleScannerParams = 76, //maybe not just vehicle
        Unk77 = 77,
        Unk78 = 78,
        Unk79 = 79,
        Unk80 = 80,
        Unk81 = 81,
        Unk82 = 82,
        Unk83 = 83, //something to do with animals
        Unk84 = 84,
        Unk85 = 85,
        Unk86 = 86,
        Explosion = 87,
        VehicleEngineGranular = 88, //maybe not just vehicle
        ShoreLinePool = 90,
        ShoreLineLake = 91,
        ShoreLineRiver = 92,
        ShoreLineOcean = 93,
        ShoreLineList = 94,
        Unk95 = 95,
        Unk96 = 96,
        RadioDjSpeechAction = 98,
        Unk99 = 99,
        Unk100 = 100,
        Unk101 = 101,
        FadeOutRadioAction = 102,
        FadeInRadioAction = 103,
        ForceRadioTrackAction = 104,
        Unk105 = 105,
        Unk106 = 106,
        Unk107 = 107,
        Unk108 = 108,
        Unk109 = 109,
        Unk110 = 110,
        Unk111 = 111,
        Unk112 = 112,
        Unk113 = 113,
        Unk114 = 114,
        Unk115 = 115,
        Unk116 = 116,
        Unk117 = 117,
        Unk118 = 118,
        Unk119 = 119,
        RadioTrackList = 120, //radio track list?
        Unk121 = 121,
    }

    [TC(typeof(EXP))] public class Dat151RelData : RelData
    {
        public Dat151RelType Type { get; set; }
        public uint NameTableOffset { get; set; }

        public Dat151RelData(RelFile rel) : base(rel) { }
        public Dat151RelData(RelFile rel, Dat151RelType type) : base(rel)
        {
            Type = type;
            TypeID = (byte)type;
        }
        public Dat151RelData(RelData d, BinaryReader br) : base(d)
        {
            Type = (Dat151RelType)TypeID;

            br.BaseStream.Position = 0; //1 byte was read already (TypeID)

            NameTableOffset = ((br.ReadUInt32() >> 8) & 0xFFFFFF);
        }


        public void WriteTypeAndOffset(BinaryWriter bw)
        {
            var val = ((NameTableOffset & 0xFFFFFF) << 8) + TypeID;
            bw.Write(val);
        }


        public override string ToString()
        {
            return GetBaseString() + ": " + Type.ToString();
        }
    }

    [TC(typeof(EXP))] public struct Dat151HashPair
    {
        public MetaHash Hash0 { get; set; }
        public MetaHash Hash1 { get; set; }

        public Dat151HashPair(MetaHash hash0, MetaHash hash1)
        {
            Hash0 = hash0;
            Hash1 = hash1;
        }
        public void ReadXml(XmlNode node)
        {
            Hash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Hash0"));
            Hash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Hash1"));
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.StringTag(sb, indent, "Hash0", RelXml.HashString(Hash0));
            RelXml.StringTag(sb, indent, "Hash1", RelXml.HashString(Hash1));
        }
        public override string ToString()
        {
            return Hash0.ToString() + ": " + Hash1.ToString();
        }
    }


    public enum Dat151ZoneShape : uint
    {
        Box = 0,
        Sphere = 1,
        Line = 2,
    }

    [TC(typeof(EXP))] public class Dat151AmbientEmitterList : Dat151RelData
    {
        public uint EmitterCount { get; set; }
        public MetaHash[] EmitterHashes { get; set; }

        public Dat151AmbientEmitterList(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.AmbientEmitterList;
            TypeID = (byte)Type;
        }
        public Dat151AmbientEmitterList(RelData d, BinaryReader br) : base(d, br)
        {
            EmitterCount = br.ReadUInt32();
            EmitterHashes = new MetaHash[EmitterCount];
            for (int i = 0; i < EmitterCount; i++)
            {
                EmitterHashes[i] = br.ReadUInt32();
            }

            long bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { } //no hits here


        }
        public override void Write(BinaryWriter bw)
        {
            //base.Write(bw);
            WriteTypeAndOffset(bw);

            bw.Write(EmitterCount);
            for (int i = 0; i < EmitterCount; i++)
            {
                bw.Write(EmitterHashes[i]);
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (EmitterCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Emitters");
                var cind = indent + 1;
                for (int i = 0; i < EmitterCount; i++)
                {
                    var iname = RelXml.HashString(EmitterHashes[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "Emitters");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Emitters");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode = node.SelectSingleNode("Emitters");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    EmitterCount = (uint)vlist.Count;
                    EmitterHashes = vlist.ToArray();
                }
            }
        }
    }
    [TC(typeof(EXP))] public class Dat151AmbientZone : Dat151RelData
    {
        public FlagsUint Flags0 { get; set; }
        public Dat151ZoneShape Shape { get; set; }
        public FlagsUint Flags1 { get; set; }
        public Vector3 OuterPos { get; set; }
        public float Unused01 { get; set; }
        public Vector3 OuterSize { get; set; }
        public float Unused02 { get; set; }
        public Vector4 OuterVec1 { get; set; }
        public Vector4 OuterVec2 { get; set; }
        public uint OuterAngle { get; set; }
        public Vector3 OuterVec3 { get; set; }
        public Vector3 InnerPos { get; set; }
        public float Unused06 { get; set; }
        public Vector3 InnerSize { get; set; }
        public float Unused07 { get; set; }
        public Vector4 InnerVec1 { get; set; }
        public Vector4 InnerVec2 { get; set; }
        public uint InnerAngle { get; set; }
        public Vector3 InnerVec3 { get; set; }
        public Vector4 UnkVec1 { get; set; }
        public Vector4 UnkVec2 { get; set; }
        public MetaHash UnkHash0 { get; set; }
        public MetaHash UnkHash1 { get; set; }
        public Vector2 UnkVec3 { get; set; }
        public FlagsUint Flags2 { get; set; }
        public byte Unk14 { get; set; }
        public byte Unk15 { get; set; }
        public byte HashesCount { get; set; }
        public byte Unk16 { get; set; }
        public MetaHash[] Hashes { get; set; }

        public uint ExtParamsCount { get; set; }
        public ExtParam[] ExtParams { get; set; }
        public struct ExtParam
        {
            public MetaHash Hash { get; set; }
            public float Value { get; set; }
            public ExtParam(BinaryReader br)
            {
                Hash = br.ReadUInt32();
                Value = br.ReadSingle();
            }
            public void Write(BinaryWriter bw)
            {
                bw.Write(Hash);
                bw.Write(Value);
            }
            public void WriteXml(StringBuilder sb, int indent)
            {
                RelXml.StringTag(sb, indent, "Hash", RelXml.HashString(Hash));
                RelXml.ValueTag(sb, indent, "Value", FloatUtil.ToString(Value));
            }
            public void ReadXml(XmlNode node)
            {
                Hash = XmlRel.GetHash(Xml.GetChildInnerText(node, "Hash"));
                Value = Xml.GetChildFloatAttribute(node, "Value", "value");
            }
            public override string ToString()
            {
                return Hash.ToString() + ": " + FloatUtil.ToString(Value);
            }
        }



        public Dat151AmbientZone(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.AmbientZone;
            TypeID = (byte)Type;
        }
        public Dat151AmbientZone(RelData d, BinaryReader br) : base(d, br)
        {
            Flags0 = br.ReadUInt32();
            Shape = (Dat151ZoneShape)br.ReadUInt32();
            Flags1 = br.ReadUInt32();
            OuterPos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unused01 = br.ReadSingle();
            OuterSize = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unused02 = br.ReadSingle();
            OuterVec1 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            OuterVec2 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            OuterAngle = br.ReadUInt32();//###
            OuterVec3 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            InnerPos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unused06 = br.ReadSingle();
            InnerSize = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unused07 = br.ReadSingle();
            InnerVec1 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            InnerVec2 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            InnerAngle = br.ReadUInt32();//###
            InnerVec3 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            UnkVec1 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            UnkVec2 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            UnkHash0 = br.ReadUInt32();
            UnkHash1 = br.ReadUInt32();
            UnkVec3 = new Vector2(br.ReadSingle(), br.ReadSingle());

            Flags2 = br.ReadUInt32();
            Unk14 = br.ReadByte();
            Unk15 = br.ReadByte();
            HashesCount = br.ReadByte();
            Unk16 = br.ReadByte();
            Hashes = new MetaHash[HashesCount];
            for (int i = 0; i < HashesCount; i++)
            {
                Hashes[i] = br.ReadUInt32();
            }

            ExtParamsCount = br.ReadUInt32();
            ExtParams = new ExtParam[ExtParamsCount];
            for (int i = 0; i < ExtParamsCount; i++)
            {
                ExtParams[i] = new ExtParam(br);
            }
            if (ExtParamsCount != 0)
            { }


            #region testing

            var data = this.Data;


            long bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            {
                //byte[] remainder = br.ReadBytes((int)bytesleft);
                //for (int i = 0; i < remainder.Length; i++)
                //{
                //    if (remainder[i] != 0)
                //    { } //no hits here! probably got everything, i'm assuming the block is padded to 0x10 or something.
                //}
            }


            //RecVec(Pos01);//debug coords output
            //RecVec(Pos06);


            if (Unused01 != 0)
            { }//no hit
            if (Unused02 != 0)
            { }//no hit
            if (Unused06 != 0)
            { }//no hit
            if (Unused07 != 0)
            { }//no hit
            if (Shape != 0)
            { }//eg 1, 2
            if (Flags1 != 0)
            { }//no hit
            if (OuterAngle > 360)
            { }//no hit
            if (InnerAngle > 360)
            { }//no hit
            if (Flags2 != 0)
            { }//eg 0xAE64583B, 0x61083310, 0xCAE96294, 0x1C376176

            if (UnkHash0 != 0)
            { }
            if (UnkHash1 != 0)
            { }

            #endregion

        }
        public override void Write(BinaryWriter bw)
        {
            //base.Write(bw);
            WriteTypeAndOffset(bw);

            bw.Write(Flags0);
            bw.Write((uint)Shape);
            bw.Write(Flags1);
            bw.Write(OuterPos.X);
            bw.Write(OuterPos.Y);
            bw.Write(OuterPos.Z);
            bw.Write(Unused01);
            bw.Write(OuterSize.X);
            bw.Write(OuterSize.Y);
            bw.Write(OuterSize.Z);
            bw.Write(Unused02);
            bw.Write(OuterVec1.X);
            bw.Write(OuterVec1.Y);
            bw.Write(OuterVec1.Z);
            bw.Write(OuterVec1.W);
            bw.Write(OuterVec2.X);
            bw.Write(OuterVec2.Y);
            bw.Write(OuterVec2.Z);
            bw.Write(OuterVec2.W);
            bw.Write(OuterAngle);//###
            bw.Write(OuterVec3.X);
            bw.Write(OuterVec3.Y);
            bw.Write(OuterVec3.Z);
            bw.Write(InnerPos.X);
            bw.Write(InnerPos.Y);
            bw.Write(InnerPos.Z);
            bw.Write(Unused06);
            bw.Write(InnerSize.X);
            bw.Write(InnerSize.Y);
            bw.Write(InnerSize.Z);
            bw.Write(Unused07);
            bw.Write(InnerVec1.X);
            bw.Write(InnerVec1.Y);
            bw.Write(InnerVec1.Z);
            bw.Write(InnerVec1.W);
            bw.Write(InnerVec2.X);
            bw.Write(InnerVec2.Y);
            bw.Write(InnerVec2.Z);
            bw.Write(InnerVec2.W);
            bw.Write(InnerAngle);//###
            bw.Write(InnerVec3.X);
            bw.Write(InnerVec3.Y);
            bw.Write(InnerVec3.Z);
            bw.Write(UnkVec1.X);
            bw.Write(UnkVec1.Y);
            bw.Write(UnkVec1.Z);
            bw.Write(UnkVec1.W);
            bw.Write(UnkVec2.X);
            bw.Write(UnkVec2.Y);
            bw.Write(UnkVec2.Z);
            bw.Write(UnkVec2.W);
            bw.Write(UnkHash0);
            bw.Write(UnkHash1);
            bw.Write(UnkVec3.X);
            bw.Write(UnkVec3.Y);

            bw.Write(Flags2);
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(HashesCount);
            bw.Write(Unk16);
            for (int i = 0; i < HashesCount; i++)
            {
                bw.Write(Hashes[i]);
            }

            bw.Write(ExtParamsCount);
            for (int i = 0; i < ExtParamsCount; i++)
            {
                ExtParams[i].Write(bw);
            }
            if (ExtParamsCount != 0)
            { }

            while ((bw.BaseStream.Position & 0xF) != 0) bw.Write((byte)0); //pad out to next 16 bytes

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Flags0", "0x" + Flags0.Hex);
            RelXml.StringTag(sb, indent, "Shape", Shape.ToString());
            RelXml.ValueTag(sb, indent, "Flags1", "0x" + Flags1.Hex);
            RelXml.SelfClosingTag(sb, indent, "OuterPos " + FloatUtil.GetVector3XmlString(OuterPos));
            RelXml.SelfClosingTag(sb, indent, "OuterSize " + FloatUtil.GetVector3XmlString(OuterSize));
            RelXml.SelfClosingTag(sb, indent, "OuterVec1 " + FloatUtil.GetVector4XmlString(OuterVec1));
            RelXml.SelfClosingTag(sb, indent, "OuterVec2 " + FloatUtil.GetVector4XmlString(OuterVec2));
            RelXml.ValueTag(sb, indent, "OuterAngle", OuterAngle.ToString());
            RelXml.SelfClosingTag(sb, indent, "OuterVec3 " + FloatUtil.GetVector3XmlString(OuterVec3));
            RelXml.SelfClosingTag(sb, indent, "InnerPos " + FloatUtil.GetVector3XmlString(InnerPos));
            RelXml.SelfClosingTag(sb, indent, "InnerSize " + FloatUtil.GetVector3XmlString(InnerSize));
            RelXml.SelfClosingTag(sb, indent, "InnerVec1 " + FloatUtil.GetVector4XmlString(InnerVec1));
            RelXml.SelfClosingTag(sb, indent, "InnerVec2 " + FloatUtil.GetVector4XmlString(InnerVec2));
            RelXml.ValueTag(sb, indent, "InnerAngle", InnerAngle.ToString());
            RelXml.SelfClosingTag(sb, indent, "InnerVec3 " + FloatUtil.GetVector3XmlString(InnerVec3));
            RelXml.SelfClosingTag(sb, indent, "UnkVec1 " + FloatUtil.GetVector4XmlString(UnkVec1));
            RelXml.SelfClosingTag(sb, indent, "UnkVec2 " + FloatUtil.GetVector4XmlString(UnkVec2));
            RelXml.StringTag(sb, indent, "UnkHash0", RelXml.HashString(UnkHash0));
            RelXml.StringTag(sb, indent, "UnkHash1", RelXml.HashString(UnkHash1));
            RelXml.SelfClosingTag(sb, indent, "UnkVec3 " + FloatUtil.GetVector2XmlString(UnkVec3));
            RelXml.ValueTag(sb, indent, "Flags2", "0x" + Flags2.Hex);
            RelXml.ValueTag(sb, indent, "Unk14", Unk14.ToString());
            RelXml.ValueTag(sb, indent, "Unk15", Unk15.ToString());
            RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
            if (HashesCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Hashes");
                var cind = indent + 1;
                for (int i = 0; i < HashesCount; i++)
                {
                    var iname = RelXml.HashString(Hashes[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "Hashes");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Hashes");
            }
            if (ExtParamsCount > 0)
            {
                RelXml.OpenTag(sb, indent, "ExtParams");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < ExtParamsCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    ExtParams[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "ExtParams");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "ExtParams");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Flags0 = Xml.GetChildUIntAttribute(node, "Flags0", "value");
            Shape = Xml.GetEnumValue<Dat151ZoneShape>(Xml.GetChildInnerText(node, "Shape"));
            Flags1 = Xml.GetChildUIntAttribute(node, "Flags1", "value");
            OuterPos = Xml.GetChildVector3Attributes(node, "OuterPos", "x", "y", "z");
            OuterSize = Xml.GetChildVector3Attributes(node, "OuterSize", "x", "y", "z");
            OuterVec1 = Xml.GetChildVector4Attributes(node, "OuterVec1", "x", "y", "z", "w");
            OuterVec2 = Xml.GetChildVector4Attributes(node, "OuterVec2", "x", "y", "z", "w");
            OuterAngle = Xml.GetChildUIntAttribute(node, "OuterAngle", "value");
            OuterVec3 = Xml.GetChildVector3Attributes(node, "OuterVec3", "x", "y", "z");
            InnerPos = Xml.GetChildVector3Attributes(node, "InnerPos", "x", "y", "z");
            InnerSize = Xml.GetChildVector3Attributes(node, "InnerSize", "x", "y", "z");
            InnerVec1 = Xml.GetChildVector4Attributes(node, "InnerVec1", "x", "y", "z", "w");
            InnerVec2 = Xml.GetChildVector4Attributes(node, "InnerVec2", "x", "y", "z", "w");
            InnerAngle = Xml.GetChildUIntAttribute(node, "InnerAngle", "value");
            InnerVec3 = Xml.GetChildVector3Attributes(node, "InnerVec3", "x", "y", "z");
            UnkVec1 = Xml.GetChildVector4Attributes(node, "UnkVec1", "x", "y", "z", "w");
            UnkVec2 = Xml.GetChildVector4Attributes(node, "UnkVec2", "x", "y", "z", "w");
            UnkHash0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash0"));
            UnkHash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "UnkHash1"));
            UnkVec3 = Xml.GetChildVector2Attributes(node, "UnkVec3", "x", "y");
            Flags2 = Xml.GetChildUIntAttribute(node, "Flags2", "value");
            Unk14 = (byte)Xml.GetChildUIntAttribute(node, "Unk14", "value");
            Unk15 = (byte)Xml.GetChildUIntAttribute(node, "Unk15", "value");
            Unk16 = (byte)Xml.GetChildUIntAttribute(node, "Unk16", "value");
            var vnode = node.SelectSingleNode("Hashes");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    HashesCount = (byte)vlist.Count;
                    Hashes = vlist.ToArray();
                }
            }
            var vnode2 = node.SelectSingleNode("ExtParams");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<ExtParam>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new ExtParam();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ExtParamsCount = (uint)vlist.Count;
                    ExtParams = vlist.ToArray();
                }
            }

        }
    }
    [TC(typeof(EXP))] public class Dat151AmbientEmitter : Dat151RelData
    {
        public FlagsUint Flags0 { get; set; }
        public FlagsUint Flags1 { get; set; }
        public FlagsUint Flags2 { get; set; }
        public Vector3 Position { get; set; }
        public FlagsUint Flags3 { get; set; }    //0
        public MetaHash Hash1 { get; set; }
        public MetaHash Hash2 { get; set; }
        public FlagsUint Flags4 { get; set; }    //0
        public FlagsUint Flags5 { get; set; }    //0xFFFFFFFF
        public FlagsUint Flags6 { get; set; }    //0
        public float Unk01 { get; set; }        //1, 5, 100, ...
        public float InnerRad { get; set; }        //0, 4,         ...     100 ... min value?
        public float OuterRad { get; set; }        //15, 16, 12, 10, 20,   300 ... max value?
        public FlagsByte Unk02 { get; set; }
        public FlagsByte Unk03 { get; set; }    //0,1,2,3,4,5
        public FlagsByte Unk04 { get; set; }
        public FlagsByte Unk05 { get; set; }    //0,1,2,3,4,5
        public FlagsUshort Unk06 { get; set; }  //0..600
        public FlagsUshort Unk07 { get; set; }  //0..150
        public FlagsByte Unk08 { get; set; }    //0,1,2
        public FlagsByte Unk09 { get; set; }    //0,1,2
        public FlagsByte Unk10 { get; set; }    //1,2,3,4,8,255
        public FlagsByte Unk11 { get; set; }    //1,2,3,4,5,6,8,10,255
        public FlagsByte Unk12 { get; set; }    //0, 50, 80, 100
        public FlagsByte Unk13 { get; set; }    //1,2,3,5
        public ushort ExtParamsCount { get; set; } //0,1,2,4
        public ExtParam[] ExtParams { get; set; }

        public struct ExtParam
        {
            public MetaHash Hash;
            public float Value;
            public FlagsUint Flags;
            public ExtParam(BinaryReader br)
            {
                Hash = br.ReadUInt32();
                Value = br.ReadSingle();
                Flags = br.ReadUInt32();
            }
            public void Write(BinaryWriter bw)
            {
                bw.Write(Hash);
                bw.Write(Value);
                bw.Write(Flags);
            }
            public void WriteXml(StringBuilder sb, int indent)
            {
                RelXml.StringTag(sb, indent, "Hash", RelXml.HashString(Hash));
                RelXml.ValueTag(sb, indent, "Value", FloatUtil.ToString(Value));
                RelXml.ValueTag(sb, indent, "Flags", "0x" + Flags.Hex);
            }
            public void ReadXml(XmlNode node)
            {
                Hash = XmlRel.GetHash(Xml.GetChildInnerText(node, "Hash"));
                Value = Xml.GetChildFloatAttribute(node, "Value", "value");
                Flags = Xml.GetChildUIntAttribute(node, "Flags", "value");
            }
            public override string ToString()
            {
                return Hash.ToString() + ": " + FloatUtil.ToString(Value) + ": " + Flags.ToString();
            }
        }


        public Dat151AmbientEmitter(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.AmbientEmitter;
            TypeID = (byte)Type;
        }
        public Dat151AmbientEmitter(RelData d, BinaryReader br) : base(d, br)
        {
            Flags0 = br.ReadUInt32();
            Flags1 = br.ReadUInt32();
            Flags2 = br.ReadUInt32();
            Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Flags3 = br.ReadUInt32();    //0
            Hash1 = br.ReadUInt32();
            Hash2 = br.ReadUInt32();
            Flags4 = br.ReadUInt32();    //0
            Flags5 = br.ReadUInt32();    //0xFFFFFFFF
            Flags6 = br.ReadUInt32();    //0
            Unk01 = br.ReadSingle();    //1, 5, 100, ...
            InnerRad = br.ReadSingle();    //0, 4,         ...     100 ... min value?
            OuterRad = br.ReadSingle();    //15, 16, 12, 10, 20,   300 ... max value?
            Unk02 = br.ReadByte();     
            Unk03 = br.ReadByte();      //0,1,2,3,4,5
            Unk04 = br.ReadByte();     
            Unk05 = br.ReadByte();      //0,1,2,3,4,5
            Unk06 = br.ReadUInt16();    //0..600
            Unk07 = br.ReadUInt16();    //0..150
            Unk08 = br.ReadByte();      //0,1,2
            Unk09 = br.ReadByte();      //0,1,2
            Unk10 = br.ReadByte();      //1,2,3,4,8,255
            Unk11 = br.ReadByte();      //1,2,3,4,5,6,8,10,255
            Unk12 = br.ReadByte();      //0, 50, 80, 100
            Unk13 = br.ReadByte();      //1,2,3,5
            ExtParamsCount = br.ReadUInt16();  //0,1,2,4

            if (ExtParamsCount > 0)
            {
                ExtParams = new ExtParam[ExtParamsCount];
                for (int i = 0; i < ExtParamsCount; i++)
                {
                    ExtParams[i] = new ExtParam(br);
                }
                //array seems to be padded to multiples of 16 bytes. (read the rest here)
                int brem = (16 - ((ExtParamsCount * 12) % 16)) % 16;
                if (brem > 0)
                {
                    byte[] brema = br.ReadBytes(brem);
                    //for (int i = 0; i < brem; i++)
                    //{
                    //    if (brema[i] != 0)
                    //    { } //check all remaining bytes are 0 - never hit here
                    //}
                }
            }


            #region testing

            switch (Unk02.Value)//no pattern?
            {
                default:
                    break;
            }
            switch (Unk03.Value)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                    break;
                default:
                    break;
            }
            switch (Unk04.Value)//no pattern?
            {
                default:
                    break;
            }
            switch (Unk05.Value)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                    break;
                default:
                    break;
            }
            switch (Unk06.Value)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                case 12:
                case 13:
                case 14:
                case 15:
                case 16:
                case 18:
                case 20:
                case 22:
                case 24:
                case 25:
                case 26:
                case 30:
                case 32:
                case 35:
                case 40:
                case 45:
                case 48:
                case 50:
                case 51:
                case 54:
                case 55:
                case 57:
                case 60:
                case 64:
                case 65:
                case 70:
                case 75:
                case 80:
                case 90:
                case 95:
                case 97:
                case 100:
                case 120:
                case 125:
                case 130:
                case 135:
                case 140:
                case 145:
                case 150:
                case 160:
                case 170:
                case 178:
                case 180:
                case 190:
                case 200:
                case 220:
                case 225:
                case 240:
                case 245:
                case 250:
                case 300:
                case 350:
                case 500:
                case 600:
                    break;
                default:
                    break;
            }
            switch (Unk07.Value)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 12:
                case 15:
                case 17:
                case 20:
                case 21:
                case 22:
                case 25:
                case 27:
                case 30:
                case 32:
                case 35:
                case 40:
                case 50:
                case 60:
                case 100:
                case 150:
                    break;
                default:
                    break;
            }
            switch (Unk08.Value)
            {
                case 0:
                case 1:
                case 2:
                    break;
                default:
                    break;
            }
            switch (Unk09.Value)
            {
                case 0:
                case 1:
                case 2:
                    break;
                default:
                    break;
            }
            switch (Unk10.Value)
            {
                case 1:
                case 2:
                case 3:
                case 4:
                case 8:
                case 255:
                    break;
                default:
                    break;
            }
            switch (Unk11.Value)
            {
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 8:
                case 10:
                case 255:
                    break;
                default:
                    break;
            }
            switch (Unk12.Value)
            {
                case 0:
                case 50:
                case 80:
                case 100:
                    break;
                default:
                    break;
            }
            switch (Unk13.Value)
            {
                case 1:
                case 2:
                case 3:
                case 5:
                    break;
                default:
                    break;
            }
            switch (ExtParamsCount)
            {
                case 0:
                case 1:
                case 2:
                case 4:
                    break;
                default:
                    break;
            }



            //if ((Position.X != 0) || (Position.Y != 0) || (Position.Z != 0))
            //{
            //    FoundCoords.Add(FloatUtil.GetVector3String(Position) + ", " + GetNameString());
            //}

            long bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }


            #endregion

        }
        public override void Write(BinaryWriter bw)
        {
            //base.Write(bw);
            WriteTypeAndOffset(bw);

            bw.Write(Flags0);
            bw.Write(Flags1);
            bw.Write(Flags2);
            bw.Write(Position.X);
            bw.Write(Position.Y);
            bw.Write(Position.Z);
            bw.Write(Flags3);
            bw.Write(Hash1);
            bw.Write(Hash2);
            bw.Write(Flags4);
            bw.Write(Flags5);
            bw.Write(Flags6);
            bw.Write(Unk01);
            bw.Write(InnerRad);
            bw.Write(OuterRad);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(ExtParamsCount);

            if (ExtParamsCount > 0)
            {
                for (int i = 0; i < ExtParamsCount; i++)
                {
                    ExtParams[i].Write(bw);
                }
                //array seems to be padded to multiples of 16 bytes. (write the rest here)
                while ((bw.BaseStream.Position & 0xF) != 0) bw.Write((byte)0); //pad out to next 16 bytes
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Flags0", "0x" + Flags0.Hex);
            RelXml.ValueTag(sb, indent, "Flags1", "0x" + Flags1.Hex);
            RelXml.ValueTag(sb, indent, "Flags2", "0x" + Flags2.Hex);
            RelXml.SelfClosingTag(sb, indent, "Position " + FloatUtil.GetVector3XmlString(Position));
            RelXml.ValueTag(sb, indent, "Flags3", "0x" + Flags3.Hex);
            RelXml.StringTag(sb, indent, "Hash1", RelXml.HashString(Hash1));
            RelXml.StringTag(sb, indent, "Hash2", RelXml.HashString(Hash2));
            RelXml.ValueTag(sb, indent, "Flags4", "0x" + Flags4.Hex);
            RelXml.ValueTag(sb, indent, "Flags5", "0x" + Flags5.Hex);
            RelXml.ValueTag(sb, indent, "Flags6", "0x" + Flags6.Hex);
            RelXml.ValueTag(sb, indent, "Unk01", FloatUtil.ToString(Unk01));
            RelXml.ValueTag(sb, indent, "InnerRad", FloatUtil.ToString(InnerRad));
            RelXml.ValueTag(sb, indent, "OuterRad", FloatUtil.ToString(OuterRad));
            RelXml.ValueTag(sb, indent, "Unk02", Unk02.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk03", Unk03.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk04", Unk04.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk05", Unk05.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk06", Unk06.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk07", Unk07.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk08", Unk08.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk09", Unk09.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk10", Unk10.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk11", Unk11.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk12", Unk12.Value.ToString());
            RelXml.ValueTag(sb, indent, "Unk13", Unk13.Value.ToString());
            if (ExtParamsCount > 0)
            {
                RelXml.OpenTag(sb, indent, "ExtParams");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < ExtParamsCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    ExtParams[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "ExtParams");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "ExtParams");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Flags0 = Xml.GetChildUIntAttribute(node, "Flags0", "value");
            Flags1 = Xml.GetChildUIntAttribute(node, "Flags1", "value");
            Flags2 = Xml.GetChildUIntAttribute(node, "Flags2", "value");
            Position = Xml.GetChildVector3Attributes(node, "Position", "x", "y", "z");
            Flags3 = Xml.GetChildUIntAttribute(node, "Flags3", "value");
            Hash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Hash1"));
            Hash2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Hash2"));
            Flags4 = Xml.GetChildUIntAttribute(node, "Flags4", "value");
            Flags5 = Xml.GetChildUIntAttribute(node, "Flags5", "value");
            Flags6 = Xml.GetChildUIntAttribute(node, "Flags6", "value");
            Unk01 = Xml.GetChildFloatAttribute(node, "Unk01", "value");
            InnerRad = Xml.GetChildFloatAttribute(node, "InnerRad", "value");
            OuterRad = Xml.GetChildFloatAttribute(node, "OuterRad", "value");
            Unk02 = (byte)Xml.GetChildUIntAttribute(node, "Unk02", "value");
            Unk03 = (byte)Xml.GetChildUIntAttribute(node, "Unk03", "value");
            Unk04 = (byte)Xml.GetChildUIntAttribute(node, "Unk04", "value");
            Unk05 = (byte)Xml.GetChildUIntAttribute(node, "Unk05", "value");
            Unk06 = (ushort)Xml.GetChildUIntAttribute(node, "Unk06", "value");
            Unk07 = (ushort)Xml.GetChildUIntAttribute(node, "Unk07", "value");
            Unk08 = (byte)Xml.GetChildUIntAttribute(node, "Unk08", "value");
            Unk09 = (byte)Xml.GetChildUIntAttribute(node, "Unk09", "value");
            Unk10 = (byte)Xml.GetChildUIntAttribute(node, "Unk10", "value");
            Unk11 = (byte)Xml.GetChildUIntAttribute(node, "Unk11", "value");
            Unk12 = (byte)Xml.GetChildUIntAttribute(node, "Unk12", "value");
            Unk13 = (byte)Xml.GetChildUIntAttribute(node, "Unk13", "value");
            var vnode2 = node.SelectSingleNode("ExtParams");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<ExtParam>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new ExtParam();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ExtParamsCount = (ushort)vlist.Count;
                    ExtParams = vlist.ToArray();
                }
            }
        }
    }
    [TC(typeof(EXP))] public class Dat151AmbientZoneList : Dat151RelData
    {
        public uint ZoneCount { get; set; }
        public MetaHash[] ZoneHashes { get; set; }

        public Dat151AmbientZoneList(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.AmbientZoneList;
            TypeID = (byte)Type;
        }
        public Dat151AmbientZoneList(RelData d, BinaryReader br) : base(d, br)
        {
            ZoneCount = br.ReadUInt32();
            ZoneHashes = new MetaHash[ZoneCount];
            for (int i = 0; i < ZoneCount; i++)
            {
                ZoneHashes[i] = br.ReadUInt32();
            }

            long bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { } //no hits here

        }
        public override void Write(BinaryWriter bw)
        {
            //base.Write(bw);
            WriteTypeAndOffset(bw);

            bw.Write(ZoneCount);
            for (int i = 0; i < ZoneCount; i++)
            {
                bw.Write(ZoneHashes[i]);
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (ZoneCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Zones");
                var cind = indent + 1;
                for (int i = 0; i < ZoneCount; i++)
                {
                    var iname = RelXml.HashString(ZoneHashes[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "Zones");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Zones");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode = node.SelectSingleNode("Zones");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    ZoneCount = (uint)vlist.Count;
                    ZoneHashes = vlist.ToArray();
                }
            }
        }
    }
    [TC(typeof(EXP))] public class Dat151StaticEmitter : Dat151RelData
    {
        public FlagsUint Flags { get; set; }//flags
        public MetaHash Unk01 { get; set; }
        public MetaHash Unk02 { get; set; }
        public Vector3 Position { get; set; }
        public float Unk06 { get; set; }
        public float Unk07 { get; set; }
        public int Unk08 { get; set; }
        public int Unk09 { get; set; }
        public int Unk10 { get; set; }
        public MetaHash Unk11 { get; set; }
        public MetaHash Unk12 { get; set; }
        public MetaHash Unk13 { get; set; }
        public float Unk14 { get; set; }
        public ushort Unk15 { get; set; }
        public ushort Unk16 { get; set; }
        public MetaHash Unk17 { get; set; }
        public MetaHash Unk18 { get; set; }
        public int Unk19 { get; set; }
        public FlagsUint Unk20 { get; set; }//0x05A00000
        public float Unk21 { get; set; }
        public float Unk22 { get; set; }

        public Dat151StaticEmitter(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.StaticEmitter;
            TypeID = (byte)Type;
        }
        public Dat151StaticEmitter(RelData d, BinaryReader br) : base(d, br)
        {
            Flags = br.ReadUInt32();//flags
            Unk01 = br.ReadUInt32();
            Unk02 = br.ReadUInt32();
            Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unk06 = br.ReadSingle();
            Unk07 = br.ReadSingle();
            Unk08 = br.ReadInt32();
            Unk09 = br.ReadInt32();
            Unk10 = br.ReadInt32();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadUInt32();
            Unk13 = br.ReadUInt32();
            Unk14 = br.ReadSingle();
            Unk15 = br.ReadUInt16();
            Unk16 = br.ReadUInt16();
            Unk17 = br.ReadUInt32();
            Unk18 = br.ReadUInt32();
            Unk19 = br.ReadInt32();
            Unk20 = br.ReadUInt32();//0x05A00000
            Unk21 = br.ReadSingle();
            Unk22 = br.ReadSingle();

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
            bw.Write(Flags);//flags
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Position.X);
            bw.Write(Position.Y);
            bw.Write(Position.Z);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(Unk16);
            bw.Write(Unk17);
            bw.Write(Unk18);
            bw.Write(Unk19);
            bw.Write(Unk20);//0x05A00000
            bw.Write(Unk21);
            bw.Write(Unk22);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Flags", "0x" + Flags.Hex);
            RelXml.StringTag(sb, indent, "Unk01", RelXml.HashString(Unk01));
            RelXml.StringTag(sb, indent, "Unk02", RelXml.HashString(Unk02));
            RelXml.SelfClosingTag(sb, indent, "Position " + FloatUtil.GetVector3XmlString(Position));
            RelXml.ValueTag(sb, indent, "Unk06", FloatUtil.ToString(Unk06));
            RelXml.ValueTag(sb, indent, "Unk07", FloatUtil.ToString(Unk07));
            RelXml.ValueTag(sb, indent, "Unk08", Unk08.ToString());
            RelXml.ValueTag(sb, indent, "Unk09", Unk09.ToString());
            RelXml.ValueTag(sb, indent, "Unk10", Unk10.ToString());
            RelXml.StringTag(sb, indent, "Unk11", RelXml.HashString(Unk11));
            RelXml.StringTag(sb, indent, "Unk12", RelXml.HashString(Unk12));
            RelXml.StringTag(sb, indent, "Unk13", RelXml.HashString(Unk13));
            RelXml.ValueTag(sb, indent, "Unk14", FloatUtil.ToString(Unk14));
            RelXml.ValueTag(sb, indent, "Unk15", Unk15.ToString());
            RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
            RelXml.StringTag(sb, indent, "Unk17", RelXml.HashString(Unk17));
            RelXml.StringTag(sb, indent, "Unk18", RelXml.HashString(Unk18));
            RelXml.ValueTag(sb, indent, "Unk19", Unk19.ToString());
            RelXml.ValueTag(sb, indent, "Unk20", "0x" + Unk20.Hex);
            RelXml.ValueTag(sb, indent, "Unk21", FloatUtil.ToString(Unk21));
            RelXml.ValueTag(sb, indent, "Unk22", FloatUtil.ToString(Unk22));
        }
        public override void ReadXml(XmlNode node)
        {
            Flags = Xml.GetChildUIntAttribute(node, "Flags", "value");
            Unk01 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk01"));
            Unk02 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk02"));
            Position = Xml.GetChildVector3Attributes(node, "Position", "x", "y", "z");
            Unk06 = Xml.GetChildFloatAttribute(node, "Unk06", "value");
            Unk07 = Xml.GetChildFloatAttribute(node, "Unk07", "value");
            Unk08 = Xml.GetChildIntAttribute(node, "Unk08", "value");
            Unk09 = Xml.GetChildIntAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildIntAttribute(node, "Unk10", "value");
            Unk11 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk11"));
            Unk12 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk12"));
            Unk13 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk13"));
            Unk14 = Xml.GetChildFloatAttribute(node, "Unk14", "value");
            Unk15 = (ushort)Xml.GetChildUIntAttribute(node, "Unk15", "value");
            Unk16 = (ushort)Xml.GetChildUIntAttribute(node, "Unk16", "value");
            Unk17 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk17"));
            Unk18 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk18"));
            Unk19 = Xml.GetChildIntAttribute(node, "Unk19", "value");
            Unk20 = Xml.GetChildUIntAttribute(node, "Unk20", "value");
            Unk21 = Xml.GetChildFloatAttribute(node, "Unk21", "value");
            Unk22 = Xml.GetChildFloatAttribute(node, "Unk22", "value");
        }
    }
    [TC(typeof(EXP))] public class Dat151Interior : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public FlagsUint Unk1 { get; set; }
        public FlagsUint Unk2 { get; set; }
        public uint RoomsCount { get; set; }
        public MetaHash[] Rooms { get; set; }

        public Dat151Interior(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Interior;
            TypeID = (byte)Type;
        }
        public Dat151Interior(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadUInt32();
            Unk2 = br.ReadUInt32();
            RoomsCount = br.ReadUInt32();
            var rooms = new MetaHash[RoomsCount];
            for (int i = 0; i < RoomsCount; i++)
            {
                rooms[i] = br.ReadUInt32();
            }
            Rooms = rooms;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(RoomsCount);
            for (int i = 0; i < RoomsCount; i++)
            {
                bw.Write(Rooms[i]);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", "0x" + Unk1.Hex);
            RelXml.ValueTag(sb, indent, "Unk2", "0x" + Unk2.Hex);
            if (RoomsCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Rooms");
                var cind = indent + 1;
                for (int i = 0; i < RoomsCount; i++)
                {
                    var iname = RelXml.HashString(Rooms[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "Rooms");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Rooms");
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < RoomsCount; i++)
            {
                offsets.Add(16 + i * 4);
            }
            return offsets.ToArray();
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildUIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildUIntAttribute(node, "Unk2", "value");
            var vnode = node.SelectSingleNode("Rooms");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    RoomsCount = (uint)vlist.Count;
                    Rooms = vlist.ToArray();
                }
            }
        }
    }
    [TC(typeof(EXP))] public class Dat151InteriorRoom : Dat151RelData
    {
        public FlagsUint Flags0 { get; set; }
        public MetaHash MloRoom { get; set; }
        public MetaHash Hash1 { get; set; }
        public uint Unk02 { get; set; }
        public float Unk03 { get; set; }
        public float Unk04 { get; set; }
        public float Unk05 { get; set; }
        public MetaHash Unk06 { get; set; }
        public float Unk07 { get; set; }
        public float Unk08 { get; set; }
        public float Unk09 { get; set; }
        public float Unk10 { get; set; }
        public float Unk11 { get; set; }
        public float Unk12 { get; set; }
        public MetaHash Unk13 { get; set; }
        public MetaHash Unk14 { get; set; }

        public Dat151InteriorRoom(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.InteriorRoom;
            TypeID = (byte)Type;
        }
        public Dat151InteriorRoom(RelData d, BinaryReader br) : base(d, br)
        {
            Flags0 = br.ReadUInt32();
            MloRoom = br.ReadUInt32();
            Hash1 = br.ReadUInt32();
            Unk02 = br.ReadUInt32();
            Unk03 = br.ReadSingle();
            Unk04 = br.ReadSingle();
            Unk05 = br.ReadSingle();
            Unk06 = br.ReadUInt32();
            Unk07 = br.ReadSingle();
            Unk08 = br.ReadSingle();
            Unk09 = br.ReadSingle();
            Unk10 = br.ReadSingle();
            Unk11 = br.ReadSingle();
            Unk12 = br.ReadSingle();
            Unk13 = br.ReadUInt32();
            Unk14 = br.ReadUInt32();

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Flags0);
            bw.Write(MloRoom);
            bw.Write(Hash1);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(Unk14);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Flags0", "0x" + Flags0.Hex);
            RelXml.StringTag(sb, indent, "MloRoom", RelXml.HashString(MloRoom));
            RelXml.StringTag(sb, indent, "Hash1", RelXml.HashString(Hash1));
            RelXml.ValueTag(sb, indent, "Unk02", Unk02.ToString());
            RelXml.ValueTag(sb, indent, "Unk03", FloatUtil.ToString(Unk03));
            RelXml.ValueTag(sb, indent, "Unk04", FloatUtil.ToString(Unk04));
            RelXml.ValueTag(sb, indent, "Unk05", FloatUtil.ToString(Unk05));
            RelXml.StringTag(sb, indent, "Unk06", RelXml.HashString(Unk06));
            RelXml.ValueTag(sb, indent, "Unk07", FloatUtil.ToString(Unk07));
            RelXml.ValueTag(sb, indent, "Unk08", FloatUtil.ToString(Unk08));
            RelXml.ValueTag(sb, indent, "Unk09", FloatUtil.ToString(Unk09));
            RelXml.ValueTag(sb, indent, "Unk10", FloatUtil.ToString(Unk10));
            RelXml.ValueTag(sb, indent, "Unk11", FloatUtil.ToString(Unk11));
            RelXml.ValueTag(sb, indent, "Unk12", FloatUtil.ToString(Unk12));
            RelXml.StringTag(sb, indent, "Unk13", RelXml.HashString(Unk13));
            RelXml.StringTag(sb, indent, "Unk14", RelXml.HashString(Unk14));
        }
        public override void ReadXml(XmlNode node)
        {
            Flags0 = Xml.GetChildUIntAttribute(node, "Flags0", "value");
            MloRoom = XmlRel.GetHash(Xml.GetChildInnerText(node, "MloRoom"));
            Hash1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Hash1"));
            Unk02 = Xml.GetChildUIntAttribute(node, "Unk02", "value");
            Unk03 = Xml.GetChildFloatAttribute(node, "Unk03", "value");
            Unk04 = Xml.GetChildFloatAttribute(node, "Unk04", "value");
            Unk05 = Xml.GetChildFloatAttribute(node, "Unk05", "value");
            Unk06 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk06"));
            Unk07 = Xml.GetChildFloatAttribute(node, "Unk07", "value");
            Unk08 = Xml.GetChildFloatAttribute(node, "Unk08", "value");
            Unk09 = Xml.GetChildFloatAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildFloatAttribute(node, "Unk10", "value");
            Unk11 = Xml.GetChildFloatAttribute(node, "Unk11", "value");
            Unk12 = Xml.GetChildFloatAttribute(node, "Unk12", "value");
            Unk13 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk13"));
            Unk14 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk14"));
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 8 };
        }
    }
    [TC(typeof(EXP))] public class Dat151RadioStations : Dat151RelData
    {
        public uint StationsCount { get; set; }
        public MetaHash[] Stations { get; set; }

        public Dat151RadioStations(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.RadioStations;
            TypeID = (byte)Type;
        }
        public Dat151RadioStations(RelData d, BinaryReader br) : base(d, br)
        {
            StationsCount = br.ReadUInt32();
            var tracks = new MetaHash[StationsCount];
            for (int i = 0; i < StationsCount; i++)
            {
                tracks[i] = br.ReadUInt32();
            }
            Stations = tracks;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(StationsCount);
            for (int i = 0; i < StationsCount; i++)
            {
                bw.Write(Stations[i]);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (StationsCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Stations");
                var cind = indent + 1;
                for (int i = 0; i < StationsCount; i++)
                {
                    var iname = RelXml.HashString(Stations[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "Stations");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Stations");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode = node.SelectSingleNode("Stations");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    StationsCount = (uint)vlist.Count;
                    Stations = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < StationsCount; i++)
            {
                offsets.Add(4 + i * 4);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151RadioStation : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; }
        public uint Unk01 { get; set; }
        public uint Unk02 { get; set; }
        public ushort Unk03 { get; set; }
        public ushort Unk04 { get; set; }
        public uint Unk05 { get; set; }//what is this? maybe shorts
        public uint Unk06 { get; set; }//what is this? maybe shorts
        public ushort Unk07 { get; set; }
        public ushort Unk08 { get; set; }
        public uint Unk09 { get; set; }//what is this? maybe shorts
        public uint Unk10 { get; set; }//what is this? maybe shorts
        public uint Unk11 { get; set; }
        public uint Unk12 { get; set; }
        public uint Unk13 { get; set; }
        public uint AudioTracksCount { get; set; }
        public MetaHash[] AudioTracks { get; set; }

        public Dat151RadioStation(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.RadioStation;
            TypeID = (byte)Type;
        }
        public Dat151RadioStation(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32();
            Unk01 = br.ReadUInt32();
            Unk02 = br.ReadUInt32();
            Unk03 = br.ReadUInt16();
            Unk04 = br.ReadUInt16();
            Unk05 = br.ReadUInt32();
            Unk06 = br.ReadUInt32();
            Unk07 = br.ReadUInt16();
            Unk08 = br.ReadUInt16();
            Unk09 = br.ReadUInt32();
            Unk10 = br.ReadUInt32();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadUInt32();
            Unk13 = br.ReadUInt32();
            AudioTracksCount = br.ReadUInt32();
            var tracks = new MetaHash[AudioTracksCount];
            for (int i = 0; i < AudioTracksCount; i++)
            {
                tracks[i] = br.ReadUInt32();
            }
            AudioTracks = tracks;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk00);
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(AudioTracksCount);
            for (int i = 0; i < AudioTracksCount; i++)
            {
                bw.Write(AudioTracks[i]);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            RelXml.ValueTag(sb, indent, "Unk01", Unk01.ToString());
            RelXml.ValueTag(sb, indent, "Unk02", Unk02.ToString());
            RelXml.ValueTag(sb, indent, "Unk03", Unk03.ToString());
            RelXml.ValueTag(sb, indent, "Unk04", Unk04.ToString());
            RelXml.ValueTag(sb, indent, "Unk05", Unk05.ToString());
            RelXml.ValueTag(sb, indent, "Unk06", Unk06.ToString());
            RelXml.ValueTag(sb, indent, "Unk07", Unk07.ToString());
            RelXml.ValueTag(sb, indent, "Unk08", Unk08.ToString());
            RelXml.ValueTag(sb, indent, "Unk09", Unk09.ToString());
            RelXml.ValueTag(sb, indent, "Unk10", Unk10.ToString());
            RelXml.ValueTag(sb, indent, "Unk11", Unk11.ToString());
            RelXml.ValueTag(sb, indent, "Unk12", Unk12.ToString());
            RelXml.ValueTag(sb, indent, "Unk13", Unk13.ToString());
            if (AudioTracksCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioTracks");
                var cind = indent + 1;
                for (int i = 0; i < AudioTracksCount; i++)
                {
                    var iname = RelXml.HashString(AudioTracks[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "AudioTracks");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioTracks");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            Unk01 = Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = Xml.GetChildUIntAttribute(node, "Unk02", "value");
            Unk03 = (ushort)Xml.GetChildUIntAttribute(node, "Unk03", "value");
            Unk04 = (ushort)Xml.GetChildUIntAttribute(node, "Unk04", "value");
            Unk05 = Xml.GetChildUIntAttribute(node, "Unk05", "value");
            Unk06 = Xml.GetChildUIntAttribute(node, "Unk06", "value");
            Unk07 = (ushort)Xml.GetChildUIntAttribute(node, "Unk07", "value");
            Unk08 = (ushort)Xml.GetChildUIntAttribute(node, "Unk08", "value");
            Unk09 = Xml.GetChildUIntAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildUIntAttribute(node, "Unk10", "value");
            Unk11 = Xml.GetChildUIntAttribute(node, "Unk11", "value");
            Unk12 = Xml.GetChildUIntAttribute(node, "Unk12", "value");
            Unk13 = Xml.GetChildUIntAttribute(node, "Unk13", "value");
            var vnode = node.SelectSingleNode("AudioTracks");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    AudioTracksCount = (uint)vlist.Count;
                    AudioTracks = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioTracksCount; i++)
            {
                offsets.Add(52 + i * 4);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151RadioMusic : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; }
        public byte MusicType { get; set; }//maybe int?
        public uint Unk01 { get; set; }
        public uint Unk02 { get; set; }
        public byte Unk03 { get; set; }
        public uint Unk04 { get; set; }
        public uint Unk05 { get; set; }
        public uint Unk06 { get; set; }
        public uint Unk07 { get; set; }
        public uint Unk08 { get; set; }
        public uint Unk09 { get; set; }
        public uint Unk10 { get; set; }
        public uint Unk11 { get; set; }
        public uint Unk12 { get; set; }
        public uint Unk13 { get; set; }
        public uint Unk14 { get; set; }
        public uint Unk15 { get; set; }
        public ushort Unk16 { get; set; }
        public uint PlaylistCount { get; set; }
        public Dat151HashPair[] Playlists { get; set; }


        public Dat151RadioMusic(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.RadioMusic;
            TypeID = (byte)Type;
        }
        public Dat151RadioMusic(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32();
            MusicType = br.ReadByte();
            Unk01 = br.ReadUInt32();
            Unk02 = br.ReadUInt32();
            Unk03 = br.ReadByte();
            Unk04 = br.ReadUInt32();
            Unk05 = br.ReadUInt32();
            Unk06 = br.ReadUInt32();
            Unk07 = br.ReadUInt32();
            Unk08 = br.ReadUInt32();
            Unk09 = br.ReadUInt32();
            Unk10 = br.ReadUInt32();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadUInt32();
            Unk13 = br.ReadUInt32();
            Unk14 = br.ReadUInt32();
            Unk15 = br.ReadUInt32();
            Unk16 = br.ReadUInt16();
            PlaylistCount = br.ReadUInt32();

            Dat151HashPair[] items = new Dat151HashPair[PlaylistCount];
            for (int i = 0; i < PlaylistCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            this.Playlists = items;


            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk00);
            bw.Write(MusicType);
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(Unk16);
            bw.Write(PlaylistCount);
            for (int i = 0; i < PlaylistCount; i++)
            {
                bw.Write(Playlists[i].Hash0);
                bw.Write(Playlists[i].Hash1);
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            RelXml.ValueTag(sb, indent, "MusicType", MusicType.ToString());
            RelXml.ValueTag(sb, indent, "Unk01", Unk01.ToString());
            RelXml.ValueTag(sb, indent, "Unk02", Unk02.ToString());
            RelXml.ValueTag(sb, indent, "Unk03", Unk03.ToString());
            RelXml.ValueTag(sb, indent, "Unk04", Unk04.ToString());
            RelXml.ValueTag(sb, indent, "Unk05", Unk05.ToString());
            RelXml.ValueTag(sb, indent, "Unk06", Unk06.ToString());
            RelXml.ValueTag(sb, indent, "Unk07", Unk07.ToString());
            RelXml.ValueTag(sb, indent, "Unk08", Unk08.ToString());
            RelXml.ValueTag(sb, indent, "Unk09", Unk09.ToString());
            RelXml.ValueTag(sb, indent, "Unk10", Unk10.ToString());
            RelXml.ValueTag(sb, indent, "Unk11", Unk11.ToString());
            RelXml.ValueTag(sb, indent, "Unk12", Unk12.ToString());
            RelXml.ValueTag(sb, indent, "Unk13", Unk13.ToString());
            RelXml.ValueTag(sb, indent, "Unk14", Unk14.ToString());
            RelXml.ValueTag(sb, indent, "Unk15", Unk15.ToString());
            RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
            if (PlaylistCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Playlists");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < PlaylistCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Playlists[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Playlists");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Playlists");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            MusicType = (byte)Xml.GetChildUIntAttribute(node, "MusicType", "value");
            Unk01 = Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = Xml.GetChildUIntAttribute(node, "Unk02", "value");
            Unk03 = (byte)Xml.GetChildUIntAttribute(node, "Unk03", "value");
            Unk04 = Xml.GetChildUIntAttribute(node, "Unk04", "value");
            Unk05 = Xml.GetChildUIntAttribute(node, "Unk05", "value");
            Unk06 = Xml.GetChildUIntAttribute(node, "Unk06", "value");
            Unk07 = Xml.GetChildUIntAttribute(node, "Unk07", "value");
            Unk08 = Xml.GetChildUIntAttribute(node, "Unk08", "value");
            Unk09 = Xml.GetChildUIntAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildUIntAttribute(node, "Unk10", "value");
            Unk11 = Xml.GetChildUIntAttribute(node, "Unk11", "value");
            Unk12 = Xml.GetChildUIntAttribute(node, "Unk12", "value");
            Unk13 = Xml.GetChildUIntAttribute(node, "Unk13", "value");
            Unk14 = Xml.GetChildUIntAttribute(node, "Unk14", "value");
            Unk15 = Xml.GetChildUIntAttribute(node, "Unk15", "value");
            Unk16 = (ushort)Xml.GetChildUIntAttribute(node, "Unk16", "value");
            var vnode2 = node.SelectSingleNode("Playlists");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    PlaylistCount = (uint)vlist.Count;
                    Playlists = vlist.ToArray();
                }
            }
        }
    }
    [TC(typeof(EXP))] public class Dat151RadioTrackList : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; }
        public uint TrackCount { get; set; }
        public Dat151HashPair[] Tracks { get; set; }

        public Dat151RadioTrackList(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.RadioTrackList;
            TypeID = (byte)Type;
        }
        public Dat151RadioTrackList(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32();
            TrackCount = br.ReadUInt32();
            Tracks = new Dat151HashPair[TrackCount];
            for (int i = 0; i < TrackCount; i++)
            {
                Tracks[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk00);
            bw.Write(TrackCount);
            for (int i = 0; i < TrackCount; i++)
            {
                bw.Write(Tracks[i].Hash0);
                bw.Write(Tracks[i].Hash1);
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            if (TrackCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Tracks");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < TrackCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Tracks[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Tracks");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Tracks");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            var vnode2 = node.SelectSingleNode("Tracks");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    TrackCount = (uint)vlist.Count;
                    Tracks = vlist.ToArray();
                }
            }
        }
    }

    [TC(typeof(EXP))] public class Dat151WeaponAudioItem : Dat151RelData
    {
        public MetaHash AudioTrack0 { get; set; }
        public uint AudioItemCount { get; set; }
        public Dat151HashPair[] AudioItems { get; set; }

        public Dat151WeaponAudioItem(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.WeaponAudioItem;
            TypeID = (byte)Type;
        }
        public Dat151WeaponAudioItem(RelData d, BinaryReader br) : base(d, br)
        {
            AudioTrack0 = br.ReadUInt32();
            AudioItemCount = br.ReadUInt32();

            Dat151HashPair[] items = new Dat151HashPair[AudioItemCount];
            for (int i = 0; i < AudioItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            this.AudioItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
            bw.Write(AudioTrack0);
            bw.Write(AudioItemCount);
            for (int i = 0; i < AudioItemCount; i++)
            {
                bw.Write(AudioItems[i].Hash0);
                bw.Write(AudioItems[i].Hash1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            if (AudioItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            var vnode2 = node.SelectSingleNode("AudioItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioItemCount = (uint)vlist.Count;
                    AudioItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            offsets.Add(0);
            for (uint i = 0; i < AudioItemCount; i++)
            {
                offsets.Add(12 + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151StartTrackAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk3 { get; set; }
        public MetaHash Unk4 { get; set; }
        public MetaHash AudioTrack2 { get; set; }
        public float Unk5 { get; set; }
        public int Unk6 { get; set; }
        public int Unk7 { get; set; }
        public float Unk8 { get; set; }
        public int Unk9 { get; set; }
        public uint ItemCount { get; set; }
        public Dat151HashPair[] Items { get; set; }


        public Dat151StartTrackAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.StartTrackAction;
            TypeID = (byte)Type;
        }
        public Dat151StartTrackAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadSingle();
            Unk4 = br.ReadUInt32();
            AudioTrack2 = br.ReadUInt32();
            Unk5 = br.ReadSingle();
            Unk6 = br.ReadInt32();
            Unk7 = br.ReadInt32();
            Unk8 = br.ReadSingle();
            Unk9 = br.ReadInt32();
            ItemCount = br.ReadUInt32();

            Dat151HashPair[] items = new Dat151HashPair[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            this.Items = items;

            if (Unk1 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
            bw.Write(Unk4);
            bw.Write(AudioTrack2);
            bw.Write(Unk5);
            bw.Write(Unk6);
            bw.Write(Unk7);
            bw.Write(Unk8);
            bw.Write(Unk9);
            bw.Write(ItemCount);

            for (int i = 0; i < ItemCount; i++)
            {
                bw.Write(Items[i].Hash0);
                bw.Write(Items[i].Hash1);
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.StringTag(sb, indent, "Unk4", RelXml.HashString(Unk4));
            RelXml.StringTag(sb, indent, "AudioTrack2", RelXml.HashString(AudioTrack2));
            RelXml.ValueTag(sb, indent, "Unk5", FloatUtil.ToString(Unk5));
            RelXml.ValueTag(sb, indent, "Unk6", Unk6.ToString());
            RelXml.ValueTag(sb, indent, "Unk7", Unk7.ToString());
            RelXml.ValueTag(sb, indent, "Unk8", FloatUtil.ToString(Unk8));
            RelXml.ValueTag(sb, indent, "Unk9", Unk9.ToString());
            if (ItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < ItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Items[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            Unk4 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk4"));
            AudioTrack2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack2"));
            Unk5 = Xml.GetChildFloatAttribute(node, "Unk5", "value");
            Unk6 = Xml.GetChildIntAttribute(node, "Unk6", "value");
            Unk7 = Xml.GetChildIntAttribute(node, "Unk7", "value");
            Unk8 = Xml.GetChildFloatAttribute(node, "Unk8", "value");
            Unk9 = Xml.GetChildIntAttribute(node, "Unk9", "value");
            var vnode2 = node.SelectSingleNode("Items");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ItemCount = (uint)vlist.Count;
                    Items = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 12, 16, 28 };
        }
    }
    [TC(typeof(EXP))] public class Dat151StopTrackAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk3 { get; set; }
        public int Unk4 { get; set; }

        public Dat151StopTrackAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.StopTrackAction;
            TypeID = (byte)Type;
        }
        public Dat151StopTrackAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadSingle();
            Unk4 = br.ReadInt32();

            if (Unk1 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
            bw.Write(Unk4);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.ValueTag(sb, indent, "Unk4", Unk4.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            Unk4 = Xml.GetChildIntAttribute(node, "Unk4", "value");
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 12, 16 };
        }

    }
    [TC(typeof(EXP))] public class Dat151MoodItem
    {
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk1 { get; set; }
        public float Unk2 { get; set; }
        public float Unk3 { get; set; }
        public float Unk4 { get; set; }
        public MetaHash AudioTrack2 { get; set; }
        public MetaHash AudioTrack3 { get; set; }

        public override string ToString()
        {
            return AudioTrack0.ToString();
        }

        public Dat151MoodItem()
        {
        }
        public Dat151MoodItem(BinaryReader br)
        {
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk1 = br.ReadSingle();
            Unk2 = br.ReadSingle();
            Unk3 = br.ReadSingle();
            Unk4 = br.ReadSingle();
            AudioTrack2 = br.ReadUInt32();
            AudioTrack3 = br.ReadUInt32();
        }
        public void Write(BinaryWriter bw)
        {
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(Unk3);
            bw.Write(Unk4);
            bw.Write(AudioTrack2);
            bw.Write(AudioTrack3);
        }
        public void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk1", FloatUtil.ToString(Unk1));
            RelXml.ValueTag(sb, indent, "Unk2", FloatUtil.ToString(Unk2));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.ValueTag(sb, indent, "Unk4", FloatUtil.ToString(Unk4));
            RelXml.StringTag(sb, indent, "AudioTrack2", RelXml.HashString(AudioTrack2));
            RelXml.StringTag(sb, indent, "AudioTrack3", RelXml.HashString(AudioTrack3));
        }
        public void ReadXml(XmlNode node)
        {
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk1 = Xml.GetChildFloatAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildFloatAttribute(node, "Unk2", "value");
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            Unk4 = Xml.GetChildFloatAttribute(node, "Unk4", "value");
            AudioTrack2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack2"));
            AudioTrack3 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack3"));
        }
    }
    [TC(typeof(EXP))] public class Dat151Mood : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public MetaHash Unk1 { get; set; }
        public MetaHash Unk2 { get; set; }
        public uint MoodItemCount { get; set; }
        public Dat151MoodItem[] MoodItems { get; set; }

        public Dat151Mood(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Mood;
            TypeID = (byte)Type;
        }
        public Dat151Mood(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadUInt32();
            Unk2 = br.ReadUInt32();
            MoodItemCount = br.ReadUInt32();
            var items = new Dat151MoodItem[MoodItemCount];
            for (int i = 0; i < MoodItemCount; i++)
            {
                items[i] = new Dat151MoodItem(br);
            }
            MoodItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(MoodItemCount);
            for (int i = 0; i < MoodItemCount; i++)
            {
                MoodItems[i].Write(bw);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.StringTag(sb, indent, "Unk1", RelXml.HashString(Unk1));
            RelXml.StringTag(sb, indent, "Unk2", RelXml.HashString(Unk2));
            if (MoodItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "MoodItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < MoodItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    MoodItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "MoodItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "MoodItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk1"));
            Unk2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk2"));
            var vnode2 = node.SelectSingleNode("MoodItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151MoodItem>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151MoodItem();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    MoodItemCount = (uint)vlist.Count;
                    MoodItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < MoodItemCount; i++)
            {
                var offs = 16 + i * 32; //offsets for each mood item's audio tracks
                offsets.Add(offs);
                offsets.Add(offs + 4);
                offsets.Add(offs + 24);
                offsets.Add(offs + 28);
            }
            return offsets.ToArray();
        }

    }
    [TC(typeof(EXP))] public class Dat151SetMoodAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk3 { get; set; }
        public MetaHash AudioTrack2 { get; set; }
        public float Unk4 { get; set; }
        public int Unk5 { get; set; }
        public int Unk6 { get; set; }

        public Dat151SetMoodAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.SetMoodAction;
            TypeID = (byte)Type;
        }
        public Dat151SetMoodAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadSingle();
            AudioTrack2 = br.ReadUInt32();
            Unk4 = br.ReadSingle();
            Unk5 = br.ReadInt32();
            Unk6 = br.ReadInt32();

            if (Unk1 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
            bw.Write(AudioTrack2);
            bw.Write(Unk4);
            bw.Write(Unk5);
            bw.Write(Unk6);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.StringTag(sb, indent, "AudioTrack2", RelXml.HashString(AudioTrack2));
            RelXml.ValueTag(sb, indent, "Unk4", FloatUtil.ToString(Unk4));
            RelXml.ValueTag(sb, indent, "Unk5", Unk5.ToString());
            RelXml.ValueTag(sb, indent, "Unk6", Unk6.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            AudioTrack2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack2"));
            Unk4 = Xml.GetChildFloatAttribute(node, "Unk4", "value");
            Unk5 = Xml.GetChildIntAttribute(node, "Unk5", "value");
            Unk6 = Xml.GetChildIntAttribute(node, "Unk6", "value");
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 12, 16, 24 };
        }
    }
    [TC(typeof(EXP))] public class Dat151PlayerAction : Dat151RelData
    {
        public uint AudioTrackCount { get; set; }
        public MetaHash[] AudioTracks { get; set; }

        public Dat151PlayerAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.PlayerAction;
            TypeID = (byte)Type;
        }
        public Dat151PlayerAction(RelData d, BinaryReader br) : base(d, br)
        {
            AudioTrackCount = br.ReadUInt32();
            var tracks = new MetaHash[AudioTrackCount];
            for (int i = 0; i < AudioTrackCount; i++)
            {
                tracks[i] = br.ReadUInt32();
            }
            AudioTracks = tracks;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioTrackCount);
            for (int i = 0; i < AudioTrackCount; i++)
            {
                bw.Write(AudioTracks[i]);
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (AudioTrackCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioTracks");
                var cind = indent + 1;
                for (int i = 0; i < AudioTrackCount; i++)
                {
                    var iname = RelXml.HashString(AudioTracks[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "AudioTracks");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioTracks");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode = node.SelectSingleNode("AudioTracks");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    AudioTrackCount = (uint)vlist.Count;
                    AudioTracks = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioTrackCount; i++)
            {
                var offs = 4 + i * 4; //offsets for each audio track
                offsets.Add(offs);
            }
            return offsets.ToArray();
        }

    }
    [TC(typeof(EXP))] public class Dat151StartOneShotAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk3 { get; set; }
        public MetaHash Unk4 { get; set; }
        public int Unk5 { get; set; }
        public int Unk6 { get; set; }
        public int Unk7 { get; set; }
        public int Unk8 { get; set; }

        public Dat151StartOneShotAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.StartOneShotAction;
            TypeID = (byte)Type;
        }
        public Dat151StartOneShotAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadSingle();
            Unk4 = br.ReadUInt32();
            Unk5 = br.ReadInt32();
            Unk6 = br.ReadInt32();
            Unk7 = br.ReadInt32();
            Unk8 = br.ReadInt32();

            if (Unk1 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
            bw.Write(Unk4);
            bw.Write(Unk5);
            bw.Write(Unk6);
            bw.Write(Unk7);
            bw.Write(Unk8);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.StringTag(sb, indent, "Unk4", RelXml.HashString(Unk4));
            RelXml.ValueTag(sb, indent, "Unk5", Unk5.ToString());
            RelXml.ValueTag(sb, indent, "Unk6", Unk6.ToString());
            RelXml.ValueTag(sb, indent, "Unk7", Unk7.ToString());
            RelXml.ValueTag(sb, indent, "Unk8", Unk8.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            Unk4 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk4"));
            Unk5 = Xml.GetChildIntAttribute(node, "Unk5", "value");
            Unk6 = Xml.GetChildIntAttribute(node, "Unk6", "value");
            Unk7 = Xml.GetChildIntAttribute(node, "Unk7", "value");
            Unk8 = Xml.GetChildIntAttribute(node, "Unk8", "value");
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 12, 16 };
        }
    }
    [TC(typeof(EXP))] public class Dat151StopOneShotAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public int Unk3 { get; set; }

        public Dat151StopOneShotAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.StopOneShotAction;
            TypeID = (byte)Type;
        }
        public Dat151StopOneShotAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadInt32();

            if (Unk1 != 0)
            { }
            if (Unk2 != 0)
            { }
            if (Unk3 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", Unk3.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildIntAttribute(node, "Unk3", "value");
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 12, 16 };
        }
    }
    [TC(typeof(EXP))] public class Dat151FadeInRadioAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk3 { get; set; }
        public float Unk4 { get; set; }

        public Dat151FadeInRadioAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.FadeInRadioAction;
            TypeID = (byte)Type;
        }
        public Dat151FadeInRadioAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadSingle();
            Unk4 = br.ReadSingle();

            if (Unk1 != 0)
            { }
            if (Unk2 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
            bw.Write(Unk4);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.ValueTag(sb, indent, "Unk4", FloatUtil.ToString(Unk4));
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            Unk4 = Xml.GetChildFloatAttribute(node, "Unk4", "value");
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 12, 16 };
        }
    }
    [TC(typeof(EXP))] public class Dat151FadeOutRadioAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk3 { get; set; }
        public float Unk4 { get; set; }

        public Dat151FadeOutRadioAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.FadeOutRadioAction;
            TypeID = (byte)Type;
        }
        public Dat151FadeOutRadioAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadSingle();
            Unk4 = br.ReadSingle();

            if (Unk1 != 0)
            { }
            if (Unk2 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
            bw.Write(Unk4);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.ValueTag(sb, indent, "Unk4", FloatUtil.ToString(Unk4));
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            Unk4 = Xml.GetChildFloatAttribute(node, "Unk4", "value");
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 12, 16 };
        }
    }
    [TC(typeof(EXP))] public class Dat151Mod : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; }
        public int Unk01 { get; set; }
        public int Unk02 { get; set; }
        public int Unk03 { get; set; }
        public MetaHash Unk04 { get; set; }
        public MetaHash Unk05 { get; set; }
        public MetaHash Unk06 { get; set; }
        public MetaHash Unk07 { get; set; }
        public MetaHash Unk08 { get; set; }
        public float Unk09 { get; set; }
        public float Unk10 { get; set; }
        public MetaHash Unk11 { get; set; }
        public MetaHash Unk12 { get; set; }
        public MetaHash Unk13 { get; set; }
        public MetaHash Unk14 { get; set; }
        public byte Unk15 { get; set; }
        public byte AudioTracks1Count { get; set; }
        public byte Unk16 { get; set; }
        public byte Unk17 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public Dat151HashPair[] AudioTracks1 { get; set; }
        public uint AudioTracks2Count { get; set; }
        public MetaHash[] AudioTracks2 { get; set; }


        public Dat151Mod(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Mod;
            TypeID = (byte)Type;
        }
        public Dat151Mod(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32();
            Unk01 = br.ReadInt32();
            Unk02 = br.ReadInt32();
            Unk03 = br.ReadInt32();
            Unk04 = br.ReadUInt32();
            Unk05 = br.ReadUInt32();
            Unk06 = br.ReadUInt32();
            Unk07 = br.ReadUInt32();
            Unk08 = br.ReadUInt32();
            Unk09 = br.ReadSingle();
            Unk10 = br.ReadSingle();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadUInt32();
            Unk13 = br.ReadUInt32();
            Unk14 = br.ReadUInt32();
            Unk15 = br.ReadByte();
            AudioTracks1Count = br.ReadByte();
            Unk16 = br.ReadByte();
            Unk17 = br.ReadByte();

            //byte tc1 = (byte)((Unk15) & 0xFF);
            //byte tc2 = (byte)((Unk15 >> 8) & 0xFF);
            //byte tc3 = (byte)((Unk15 >> 16) & 0xFF);
            //byte tc4 = (byte)((Unk15 >> 24) & 0xFF);

            switch (Unk15)//not sure what this is
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                    break;
                default:
                    break;
            }


            if (AudioTracks1Count == 0)
            {
                AudioTrack0 = br.ReadUInt32();
                //AudioTracks2 = new MetaHash[] { AudioTrack0 };
            }
            else //if (AudioTracks1Count > 0)
            {
                var tracks1 = new Dat151HashPair[AudioTracks1Count];
                for (int i = 0; i < AudioTracks1Count; i++)
                {
                    tracks1[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
                }
                AudioTracks1 = tracks1;

                AudioTracks2Count = br.ReadUInt32();

                var tracks2 = new MetaHash[AudioTracks2Count];
                for (int i = 0; i < AudioTracks2Count; i++)
                {
                    tracks2[i] = br.ReadUInt32();
                }
                AudioTracks2 = tracks2;
            }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk00);
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(AudioTracks1Count);
            bw.Write(Unk16);
            bw.Write(Unk17);


            if (AudioTracks1Count == 0)
            {
                bw.Write(AudioTrack0);//hrmm
            }
            else //if (AudioTracks1Count > 0)
            {
                for (int i = 0; i < AudioTracks1Count; i++)
                {
                    bw.Write(AudioTracks1[i].Hash0);
                    bw.Write(AudioTracks1[i].Hash1);
                }
                bw.Write(AudioTracks2Count);
                for (int i = 0; i < AudioTracks2Count; i++)
                {
                    bw.Write(AudioTracks2[i]);
                }
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            RelXml.ValueTag(sb, indent, "Unk01", Unk01.ToString());
            RelXml.ValueTag(sb, indent, "Unk02", Unk02.ToString());
            RelXml.ValueTag(sb, indent, "Unk03", Unk03.ToString());
            RelXml.StringTag(sb, indent, "Unk04", RelXml.HashString(Unk04));
            RelXml.StringTag(sb, indent, "Unk05", RelXml.HashString(Unk05));
            RelXml.StringTag(sb, indent, "Unk06", RelXml.HashString(Unk06));
            RelXml.StringTag(sb, indent, "Unk07", RelXml.HashString(Unk07));
            RelXml.StringTag(sb, indent, "Unk08", RelXml.HashString(Unk08));
            RelXml.ValueTag(sb, indent, "Unk09", FloatUtil.ToString(Unk09));
            RelXml.ValueTag(sb, indent, "Unk10", FloatUtil.ToString(Unk10));
            RelXml.StringTag(sb, indent, "Unk11", RelXml.HashString(Unk11));
            RelXml.StringTag(sb, indent, "Unk12", RelXml.HashString(Unk12));
            RelXml.StringTag(sb, indent, "Unk13", RelXml.HashString(Unk13));
            RelXml.StringTag(sb, indent, "Unk14", RelXml.HashString(Unk14));
            RelXml.ValueTag(sb, indent, "Unk15", Unk15.ToString());
            RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
            RelXml.ValueTag(sb, indent, "Unk17", Unk17.ToString());
            if (AudioTracks1Count == 0)
            {
                RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            }
            else
            {
                RelXml.OpenTag(sb, indent, "AudioTracks1");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioTracks1Count; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioTracks1[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioTracks1");

                if (AudioTracks2Count > 0)
                {
                    RelXml.OpenTag(sb, indent, "AudioTracks2");
                    for (int i = 0; i < AudioTracks2Count; i++)
                    {
                        var iname = RelXml.HashString(AudioTracks2[i]);
                        RelXml.StringTag(sb, cind, "Item", iname);
                    }
                    RelXml.CloseTag(sb, indent, "AudioTracks2");
                }
                else
                {
                    RelXml.SelfClosingTag(sb, indent, "AudioTracks2");
                }

            }
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            Unk01 = Xml.GetChildIntAttribute(node, "Unk01", "value");
            Unk02 = Xml.GetChildIntAttribute(node, "Unk02", "value");
            Unk03 = Xml.GetChildIntAttribute(node, "Unk03", "value");
            Unk04 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk04"));
            Unk05 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk05"));
            Unk06 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk06"));
            Unk07 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk07"));
            Unk08 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk08"));
            Unk09 = Xml.GetChildFloatAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildFloatAttribute(node, "Unk10", "value");
            Unk11 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk11"));
            Unk12 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk12"));
            Unk13 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk13"));
            Unk14 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk14"));
            Unk15 = (byte)Xml.GetChildUIntAttribute(node, "Unk15", "value");
            Unk16 = (byte)Xml.GetChildUIntAttribute(node, "Unk16", "value");
            Unk17 = (byte)Xml.GetChildUIntAttribute(node, "Unk17", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            var vnode2 = node.SelectSingleNode("AudioTracks1");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioTracks1Count = (byte)vlist.Count;
                    AudioTracks1 = vlist.ToArray();
                }
            }
            var vnode = node.SelectSingleNode("AudioTracks2");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    AudioTracks2Count = (uint)vlist.Count;
                    AudioTracks2 = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            uint offs = 64;
            if (AudioTracks1Count == 0)
            {
            }
            else //if (AudioTracks1Count > 0)
            {
                for (uint i = 0; i < AudioTracks1Count; i++)
                {
                    offsets.Add(offs);
                    offsets.Add(offs + 4);
                    offs += 8;
                }
                offs += 4;
                for (uint i = 0; i < AudioTracks2Count; i++)
                {
                    offsets.Add(offs);
                    offs += 4;
                }
            }

            return offsets.ToArray();
        }

    }
    [TC(typeof(EXP))] public class Dat151Unk117 : Dat151RelData
    {
        public MetaHash AudioTrack0 { get; set; }

        public Dat151Unk117(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk117;
            TypeID = (byte)Type;
        }
        public Dat151Unk117(RelData d, BinaryReader br) : base(d, br)
        {
            AudioTrack0 = br.ReadUInt32();

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioTrack0);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
        }
        public override void ReadXml(XmlNode node)
        {
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 0 };
        }
    }
    [TC(typeof(EXP))] public class Dat151Entity : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; }
        public MetaHash Unk01 { get; set; }
        public MetaHash Unk02 { get; set; }
        public MetaHash Unk03 { get; set; }
        public MetaHash Unk04 { get; set; }
        public MetaHash Unk05 { get; set; }
        public MetaHash Unk06 { get; set; }
        public MetaHash Unk07 { get; set; }
        public MetaHash Unk08 { get; set; }
        public MetaHash Unk09 { get; set; }
        public MetaHash Unk10 { get; set; }
        public MetaHash Unk11 { get; set; }
        public MetaHash Unk12 { get; set; }
        public MetaHash Unk13 { get; set; }
        public MetaHash Unk14 { get; set; }
        public int Unk15 { get; set; }
        public int Unk16 { get; set; }
        public int Unk17 { get; set; }
        public int Unk18 { get; set; }
        public float Unk19 { get; set; }
        public int Unk20 { get; set; }
        public float Unk21 { get; set; }
        public float Unk22 { get; set; }
        public float Unk23 { get; set; }
        public float Unk24 { get; set; }
        public float Unk25 { get; set; }
        public float Unk26 { get; set; }
        public int Unk27 { get; set; }
        public MetaHash Unk28 { get; set; }
        public float Unk29 { get; set; }
        public float Unk30 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public int Unk31 { get; set; }
        public MetaHash Unk32 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public MetaHash AudioTrack2 { get; set; }
        public MetaHash Unk33 { get; set; }
        public MetaHash Unk34 { get; set; }
        public MetaHash Unk35 { get; set; }
        public MetaHash Unk36 { get; set; }
        public MetaHash Unk37 { get; set; }
        public MetaHash Unk38 { get; set; }
        public MetaHash Unk39 { get; set; }
        public MetaHash Unk40 { get; set; }
        public MetaHash Unk41 { get; set; }
        public MetaHash Unk42 { get; set; }
        public MetaHash Unk43 { get; set; }
        public MetaHash Unk44 { get; set; }
        public MetaHash Unk45 { get; set; }
        public MetaHash Unk46 { get; set; }
        public MetaHash Unk47 { get; set; }
        public MetaHash Unk48 { get; set; }
        public MetaHash Unk49 { get; set; }
        public MetaHash Unk50 { get; set; }
        public MetaHash Unk51 { get; set; }
        public MetaHash Unk52 { get; set; }
        public MetaHash Unk53 { get; set; }
        public MetaHash Unk54 { get; set; }
        public float Unk55 { get; set; }
        public MetaHash Unk56 { get; set; }
        public MetaHash Unk57 { get; set; }
        public int Unk58 { get; set; }
        public int Unk59 { get; set; }
        public MetaHash Unk60 { get; set; }
        public int Unk61 { get; set; }
        public int Unk62 { get; set; }
        public MetaHash Unk63 { get; set; }
        public MetaHash Unk64 { get; set; }
        public MetaHash Unk65 { get; set; }
        public int Unk66 { get; set; }
        public MetaHash Unk67 { get; set; }
        public MetaHash Unk68 { get; set; }
        public MetaHash Unk69 { get; set; }
        public MetaHash Unk70 { get; set; }
        public MetaHash Unk71 { get; set; }
        public int Unk72 { get; set; }
        public MetaHash Unk73 { get; set; }
        public MetaHash Unk74 { get; set; }
        public MetaHash Unk75 { get; set; }
        public MetaHash Unk76 { get; set; }
        public float Unk77 { get; set; }
        public MetaHash Unk78 { get; set; }
        public MetaHash Unk79 { get; set; }
        public MetaHash Unk80 { get; set; }
        public MetaHash Unk81 { get; set; }
        public MetaHash Unk82 { get; set; }
        public MetaHash Unk83 { get; set; }
        public MetaHash Unk84 { get; set; }
        public int Unk85 { get; set; }
        public MetaHash Unk86 { get; set; }
        public int Unk87 { get; set; }
        public MetaHash Unk88 { get; set; }
        public int Unk89 { get; set; }

        public Dat151Entity(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Entity;
            TypeID = (byte)Type;
        }
        public Dat151Entity(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32();
            Unk01 = br.ReadUInt32();
            Unk02 = br.ReadUInt32();
            Unk03 = br.ReadUInt32();
            Unk04 = br.ReadUInt32();
            Unk05 = br.ReadUInt32();
            Unk06 = br.ReadUInt32();
            Unk07 = br.ReadUInt32();
            Unk08 = br.ReadUInt32();
            Unk09 = br.ReadUInt32();
            Unk10 = br.ReadUInt32();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadUInt32();
            Unk13 = br.ReadUInt32();
            Unk14 = br.ReadUInt32();
            Unk15 = br.ReadInt32();
            Unk16 = br.ReadInt32();
            Unk17 = br.ReadInt32();
            Unk18 = br.ReadInt32();
            Unk19 = br.ReadSingle();
            Unk20 = br.ReadInt32();
            Unk21 = br.ReadSingle();
            Unk22 = br.ReadSingle();
            Unk23 = br.ReadSingle();
            Unk24 = br.ReadSingle();
            Unk25 = br.ReadSingle();
            Unk26 = br.ReadSingle();
            Unk27 = br.ReadInt32();
            Unk28 = br.ReadUInt32();
            Unk29 = br.ReadSingle();
            Unk30 = br.ReadSingle();
            AudioTrack0 = br.ReadUInt32();
            Unk31 = br.ReadInt32();
            Unk32 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            AudioTrack2 = br.ReadUInt32();
            Unk33 = br.ReadUInt32();
            Unk34 = br.ReadUInt32();
            Unk35 = br.ReadUInt32();
            Unk36 = br.ReadUInt32();
            Unk37 = br.ReadUInt32();
            Unk38 = br.ReadUInt32();
            Unk39 = br.ReadUInt32();
            Unk40 = br.ReadUInt32();
            Unk41 = br.ReadUInt32();
            Unk42 = br.ReadUInt32();
            Unk43 = br.ReadUInt32();
            Unk44 = br.ReadUInt32();
            Unk45 = br.ReadUInt32();
            Unk46 = br.ReadUInt32();
            Unk47 = br.ReadUInt32();
            Unk48 = br.ReadUInt32();
            Unk49 = br.ReadUInt32();
            Unk50 = br.ReadUInt32();
            Unk51 = br.ReadUInt32();
            Unk52 = br.ReadUInt32();
            Unk53 = br.ReadUInt32();
            Unk54 = br.ReadUInt32();
            Unk55 = br.ReadSingle();
            Unk56 = br.ReadUInt32();
            Unk57 = br.ReadUInt32();
            Unk58 = br.ReadInt32();
            Unk59 = br.ReadInt32();
            Unk60 = br.ReadUInt32();
            Unk61 = br.ReadInt32();
            Unk62 = br.ReadInt32();
            Unk63 = br.ReadUInt32();
            Unk64 = br.ReadUInt32();
            Unk65 = br.ReadUInt32();
            Unk66 = br.ReadInt32();
            Unk67 = br.ReadUInt32();
            Unk68 = br.ReadUInt32();
            Unk69 = br.ReadUInt32();
            Unk70 = br.ReadUInt32();
            Unk71 = br.ReadUInt32();
            Unk72 = br.ReadInt32();
            Unk73 = br.ReadUInt32();
            Unk74 = br.ReadUInt32();
            Unk75 = br.ReadUInt32();
            Unk76 = br.ReadUInt32();
            Unk77 = br.ReadSingle();
            Unk78 = br.ReadUInt32();
            Unk79 = br.ReadUInt32();
            Unk80 = br.ReadUInt32();
            Unk81 = br.ReadUInt32();
            Unk82 = br.ReadUInt32();
            Unk83 = br.ReadUInt32();
            Unk84 = br.ReadUInt32();
            Unk85 = br.ReadInt32();
            Unk86 = br.ReadUInt32();
            Unk87 = br.ReadInt32();
            Unk88 = br.ReadUInt32();
            Unk89 = br.ReadInt32();

            if (Unk58 != 0)
            { }
            if (Unk61 != 0)
            { }
            if (Unk62 != 0)
            { }
            if (Unk66 != 0)
            { }
            if (Unk87 != 0)
            { }
            if (Unk89 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk00);
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(Unk16);
            bw.Write(Unk17);
            bw.Write(Unk18);
            bw.Write(Unk19);
            bw.Write(Unk20);
            bw.Write(Unk21);
            bw.Write(Unk22);
            bw.Write(Unk23);
            bw.Write(Unk24);
            bw.Write(Unk25);
            bw.Write(Unk26);
            bw.Write(Unk27);
            bw.Write(Unk28);
            bw.Write(Unk29);
            bw.Write(Unk30);
            bw.Write(AudioTrack0);
            bw.Write(Unk31);
            bw.Write(Unk32);
            bw.Write(AudioTrack1);
            bw.Write(AudioTrack2);
            bw.Write(Unk33);
            bw.Write(Unk34);
            bw.Write(Unk35);
            bw.Write(Unk36);
            bw.Write(Unk37);
            bw.Write(Unk38);
            bw.Write(Unk39);
            bw.Write(Unk40);
            bw.Write(Unk41);
            bw.Write(Unk42);
            bw.Write(Unk43);
            bw.Write(Unk44);
            bw.Write(Unk45);
            bw.Write(Unk46);
            bw.Write(Unk47);
            bw.Write(Unk48);
            bw.Write(Unk49);
            bw.Write(Unk50);
            bw.Write(Unk51);
            bw.Write(Unk52);
            bw.Write(Unk53);
            bw.Write(Unk54);
            bw.Write(Unk55);
            bw.Write(Unk56);
            bw.Write(Unk57);
            bw.Write(Unk58);
            bw.Write(Unk59);
            bw.Write(Unk60);
            bw.Write(Unk61);
            bw.Write(Unk62);
            bw.Write(Unk63);
            bw.Write(Unk64);
            bw.Write(Unk65);
            bw.Write(Unk66);
            bw.Write(Unk67);
            bw.Write(Unk68);
            bw.Write(Unk69);
            bw.Write(Unk70);
            bw.Write(Unk71);
            bw.Write(Unk72);
            bw.Write(Unk73);
            bw.Write(Unk74);
            bw.Write(Unk75);
            bw.Write(Unk76);
            bw.Write(Unk77);
            bw.Write(Unk78);
            bw.Write(Unk79);
            bw.Write(Unk80);
            bw.Write(Unk81);
            bw.Write(Unk82);
            bw.Write(Unk83);
            bw.Write(Unk84);
            bw.Write(Unk85);
            bw.Write(Unk86);
            bw.Write(Unk87);
            bw.Write(Unk88);
            bw.Write(Unk89);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            RelXml.StringTag(sb, indent, "Unk01", RelXml.HashString(Unk01));
            RelXml.StringTag(sb, indent, "Unk02", RelXml.HashString(Unk02));
            RelXml.StringTag(sb, indent, "Unk03", RelXml.HashString(Unk03));
            RelXml.StringTag(sb, indent, "Unk04", RelXml.HashString(Unk04));
            RelXml.StringTag(sb, indent, "Unk05", RelXml.HashString(Unk05));
            RelXml.StringTag(sb, indent, "Unk06", RelXml.HashString(Unk06));
            RelXml.StringTag(sb, indent, "Unk07", RelXml.HashString(Unk07));
            RelXml.StringTag(sb, indent, "Unk08", RelXml.HashString(Unk08));
            RelXml.StringTag(sb, indent, "Unk09", RelXml.HashString(Unk09));
            RelXml.StringTag(sb, indent, "Unk10", RelXml.HashString(Unk10));
            RelXml.StringTag(sb, indent, "Unk11", RelXml.HashString(Unk11));
            RelXml.StringTag(sb, indent, "Unk12", RelXml.HashString(Unk12));
            RelXml.StringTag(sb, indent, "Unk13", RelXml.HashString(Unk13));
            RelXml.StringTag(sb, indent, "Unk14", RelXml.HashString(Unk14));
            RelXml.ValueTag(sb, indent, "Unk15", Unk15.ToString());
            RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
            RelXml.ValueTag(sb, indent, "Unk17", Unk17.ToString());
            RelXml.ValueTag(sb, indent, "Unk18", Unk18.ToString());
            RelXml.ValueTag(sb, indent, "Unk19", FloatUtil.ToString(Unk19));
            RelXml.ValueTag(sb, indent, "Unk20", Unk20.ToString());
            RelXml.ValueTag(sb, indent, "Unk21", FloatUtil.ToString(Unk21));
            RelXml.ValueTag(sb, indent, "Unk22", FloatUtil.ToString(Unk22));
            RelXml.ValueTag(sb, indent, "Unk23", FloatUtil.ToString(Unk23));
            RelXml.ValueTag(sb, indent, "Unk24", FloatUtil.ToString(Unk24));
            RelXml.ValueTag(sb, indent, "Unk25", FloatUtil.ToString(Unk25));
            RelXml.ValueTag(sb, indent, "Unk26", FloatUtil.ToString(Unk26));
            RelXml.ValueTag(sb, indent, "Unk27", Unk27.ToString());
            RelXml.StringTag(sb, indent, "Unk28", RelXml.HashString(Unk28));
            RelXml.ValueTag(sb, indent, "Unk29", FloatUtil.ToString(Unk29));
            RelXml.ValueTag(sb, indent, "Unk30", FloatUtil.ToString(Unk30));
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.ValueTag(sb, indent, "Unk31", Unk31.ToString());
            RelXml.StringTag(sb, indent, "Unk32", RelXml.HashString(Unk32));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.StringTag(sb, indent, "AudioTrack2", RelXml.HashString(AudioTrack2));
            RelXml.StringTag(sb, indent, "Unk33", RelXml.HashString(Unk33));
            RelXml.StringTag(sb, indent, "Unk34", RelXml.HashString(Unk34));
            RelXml.StringTag(sb, indent, "Unk35", RelXml.HashString(Unk35));
            RelXml.StringTag(sb, indent, "Unk36", RelXml.HashString(Unk36));
            RelXml.StringTag(sb, indent, "Unk37", RelXml.HashString(Unk37));
            RelXml.StringTag(sb, indent, "Unk38", RelXml.HashString(Unk38));
            RelXml.StringTag(sb, indent, "Unk39", RelXml.HashString(Unk39));
            RelXml.StringTag(sb, indent, "Unk40", RelXml.HashString(Unk40));
            RelXml.StringTag(sb, indent, "Unk41", RelXml.HashString(Unk41));
            RelXml.StringTag(sb, indent, "Unk42", RelXml.HashString(Unk42));
            RelXml.StringTag(sb, indent, "Unk43", RelXml.HashString(Unk43));
            RelXml.StringTag(sb, indent, "Unk44", RelXml.HashString(Unk44));
            RelXml.StringTag(sb, indent, "Unk45", RelXml.HashString(Unk45));
            RelXml.StringTag(sb, indent, "Unk46", RelXml.HashString(Unk46));
            RelXml.StringTag(sb, indent, "Unk47", RelXml.HashString(Unk47));
            RelXml.StringTag(sb, indent, "Unk48", RelXml.HashString(Unk48));
            RelXml.StringTag(sb, indent, "Unk49", RelXml.HashString(Unk49));
            RelXml.StringTag(sb, indent, "Unk50", RelXml.HashString(Unk50));
            RelXml.StringTag(sb, indent, "Unk51", RelXml.HashString(Unk51));
            RelXml.StringTag(sb, indent, "Unk52", RelXml.HashString(Unk52));
            RelXml.StringTag(sb, indent, "Unk53", RelXml.HashString(Unk53));
            RelXml.StringTag(sb, indent, "Unk54", RelXml.HashString(Unk54));
            RelXml.ValueTag(sb, indent, "Unk55", FloatUtil.ToString(Unk55));
            RelXml.StringTag(sb, indent, "Unk56", RelXml.HashString(Unk56));
            RelXml.StringTag(sb, indent, "Unk57", RelXml.HashString(Unk57));
            RelXml.ValueTag(sb, indent, "Unk58", Unk58.ToString());
            RelXml.ValueTag(sb, indent, "Unk59", Unk59.ToString());
            RelXml.StringTag(sb, indent, "Unk60", RelXml.HashString(Unk60));
            RelXml.ValueTag(sb, indent, "Unk61", Unk61.ToString());
            RelXml.ValueTag(sb, indent, "Unk62", Unk62.ToString());
            RelXml.StringTag(sb, indent, "Unk63", RelXml.HashString(Unk63));
            RelXml.StringTag(sb, indent, "Unk64", RelXml.HashString(Unk64));
            RelXml.StringTag(sb, indent, "Unk65", RelXml.HashString(Unk65));
            RelXml.ValueTag(sb, indent, "Unk66", Unk66.ToString());
            RelXml.StringTag(sb, indent, "Unk67", RelXml.HashString(Unk67));
            RelXml.StringTag(sb, indent, "Unk68", RelXml.HashString(Unk68));
            RelXml.StringTag(sb, indent, "Unk69", RelXml.HashString(Unk69));
            RelXml.StringTag(sb, indent, "Unk70", RelXml.HashString(Unk70));
            RelXml.StringTag(sb, indent, "Unk71", RelXml.HashString(Unk71));
            RelXml.ValueTag(sb, indent, "Unk72", Unk72.ToString());
            RelXml.StringTag(sb, indent, "Unk73", RelXml.HashString(Unk73));
            RelXml.StringTag(sb, indent, "Unk74", RelXml.HashString(Unk74));
            RelXml.StringTag(sb, indent, "Unk75", RelXml.HashString(Unk75));
            RelXml.StringTag(sb, indent, "Unk76", RelXml.HashString(Unk76));
            RelXml.ValueTag(sb, indent, "Unk77", FloatUtil.ToString(Unk77));
            RelXml.StringTag(sb, indent, "Unk78", RelXml.HashString(Unk78));
            RelXml.StringTag(sb, indent, "Unk79", RelXml.HashString(Unk79));
            RelXml.StringTag(sb, indent, "Unk80", RelXml.HashString(Unk80));
            RelXml.StringTag(sb, indent, "Unk81", RelXml.HashString(Unk81));
            RelXml.StringTag(sb, indent, "Unk82", RelXml.HashString(Unk82));
            RelXml.StringTag(sb, indent, "Unk83", RelXml.HashString(Unk83));
            RelXml.StringTag(sb, indent, "Unk84", RelXml.HashString(Unk84));
            RelXml.ValueTag(sb, indent, "Unk85", Unk85.ToString());
            RelXml.StringTag(sb, indent, "Unk86", RelXml.HashString(Unk86));
            RelXml.ValueTag(sb, indent, "Unk87", Unk87.ToString());
            RelXml.StringTag(sb, indent, "Unk88", RelXml.HashString(Unk88));
            RelXml.ValueTag(sb, indent, "Unk89", Unk89.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            Unk01 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk01"));
            Unk02 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk02"));
            Unk03 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk03"));
            Unk04 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk04"));
            Unk05 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk05"));
            Unk06 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk06"));
            Unk07 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk07"));
            Unk08 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk08"));
            Unk09 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk09"));
            Unk10 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk10"));
            Unk11 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk11"));
            Unk12 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk12"));
            Unk13 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk13"));
            Unk14 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk14"));
            Unk15 = Xml.GetChildIntAttribute(node, "Unk15", "value");
            Unk16 = Xml.GetChildIntAttribute(node, "Unk16", "value");
            Unk17 = Xml.GetChildIntAttribute(node, "Unk17", "value");
            Unk18 = Xml.GetChildIntAttribute(node, "Unk18", "value");
            Unk19 = Xml.GetChildFloatAttribute(node, "Unk19", "value");
            Unk20 = Xml.GetChildIntAttribute(node, "Unk20", "value");
            Unk21 = Xml.GetChildFloatAttribute(node, "Unk21", "value");
            Unk22 = Xml.GetChildFloatAttribute(node, "Unk22", "value");
            Unk23 = Xml.GetChildFloatAttribute(node, "Unk23", "value");
            Unk24 = Xml.GetChildFloatAttribute(node, "Unk24", "value");
            Unk25 = Xml.GetChildFloatAttribute(node, "Unk25", "value");
            Unk26 = Xml.GetChildFloatAttribute(node, "Unk26", "value");
            Unk27 = Xml.GetChildIntAttribute(node, "Unk27", "value");
            Unk28 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk28"));
            Unk29 = Xml.GetChildFloatAttribute(node, "Unk29", "value");
            Unk30 = Xml.GetChildFloatAttribute(node, "Unk30", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            Unk31 = Xml.GetChildIntAttribute(node, "Unk31", "value");
            Unk32 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk32"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            AudioTrack2 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack2"));
            Unk33 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk33"));
            Unk34 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk34"));
            Unk35 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk35"));
            Unk36 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk36"));
            Unk37 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk37"));
            Unk38 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk38"));
            Unk39 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk39"));
            Unk40 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk40"));
            Unk41 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk41"));
            Unk42 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk42"));
            Unk43 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk43"));
            Unk44 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk44"));
            Unk45 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk45"));
            Unk46 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk46"));
            Unk47 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk47"));
            Unk48 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk48"));
            Unk49 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk49"));
            Unk50 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk50"));
            Unk51 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk51"));
            Unk52 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk52"));
            Unk53 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk53"));
            Unk54 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk54"));
            Unk55 = Xml.GetChildFloatAttribute(node, "Unk55", "value");
            Unk56 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk56"));
            Unk57 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk57"));
            Unk58 = Xml.GetChildIntAttribute(node, "Unk58", "value");
            Unk59 = Xml.GetChildIntAttribute(node, "Unk59", "value");
            Unk60 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk60"));
            Unk61 = Xml.GetChildIntAttribute(node, "Unk61", "value");
            Unk62 = Xml.GetChildIntAttribute(node, "Unk62", "value");
            Unk63 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk63"));
            Unk64 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk64"));
            Unk65 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk65"));
            Unk66 = Xml.GetChildIntAttribute(node, "Unk66", "value");
            Unk67 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk67"));
            Unk68 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk68"));
            Unk69 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk69"));
            Unk70 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk70"));
            Unk71 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk71"));
            Unk72 = Xml.GetChildIntAttribute(node, "Unk72", "value");
            Unk73 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk73"));
            Unk74 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk74"));
            Unk75 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk75"));
            Unk76 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk76"));
            Unk77 = Xml.GetChildFloatAttribute(node, "Unk77", "value");
            Unk78 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk78"));
            Unk79 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk79"));
            Unk80 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk80"));
            Unk81 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk81"));
            Unk82 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk82"));
            Unk83 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk83"));
            Unk84 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk84"));
            Unk85 = Xml.GetChildIntAttribute(node, "Unk85", "value");
            Unk86 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk86"));
            Unk87 = Xml.GetChildIntAttribute(node, "Unk87", "value");
            Unk88 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk88"));
            Unk89 = Xml.GetChildIntAttribute(node, "Unk89", "value");

        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 124, 136, 140 };
        }
    }
    [TC(typeof(EXP))] public class Dat151Collision : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; }
        public uint Unk01 { get; set; }
        public MetaHash Unk02 { get; set; }
        public MetaHash Unk03 { get; set; }
        public MetaHash Unk04 { get; set; }
        public MetaHash Unk05 { get; set; }
        public MetaHash Unk06 { get; set; }
        public MetaHash Unk07 { get; set; }
        public float Unk08 { get; set; }
        public MetaHash Unk09 { get; set; }
        public MetaHash Unk10 { get; set; }
        public MetaHash Unk11 { get; set; }
        public MetaHash Unk12 { get; set; }
        public MetaHash Unk13 { get; set; }
        public MetaHash Unk14 { get; set; }
        public MetaHash Unk15 { get; set; }
        public MetaHash Unk16 { get; set; }
        public MetaHash Unk17 { get; set; }
        public MetaHash Unk18 { get; set; }
        public MetaHash Unk19 { get; set; }
        public MetaHash Unk20 { get; set; }
        public float Unk21 { get; set; }
        public float Unk22 { get; set; }
        public float Unk23 { get; set; }
        public float Unk24 { get; set; }
        public float Unk25 { get; set; }
        public float Unk26 { get; set; }
        public MetaHash Unk27 { get; set; }
        public MetaHash Unk28 { get; set; }
        public MetaHash Unk29 { get; set; }
        public MetaHash Unk30 { get; set; }
        public MetaHash Unk31 { get; set; }
        public MetaHash Unk32 { get; set; }
        public MetaHash Unk33 { get; set; }
        public float Unk34 { get; set; }
        public float Unk35 { get; set; }
        public float Unk36 { get; set; }
        public float Unk37 { get; set; }
        public uint Unk38 { get; set; }
        public float Unk39 { get; set; }
        public float Unk40 { get; set; }
        public float Unk41 { get; set; }
        public float Unk42 { get; set; }
        public float Unk43 { get; set; }
        public float Unk44 { get; set; }
        public MetaHash Unk45 { get; set; }
        public MetaHash Unk46 { get; set; }
        public MetaHash Unk47 { get; set; }
        public MetaHash Unk48 { get; set; }
        public MetaHash Unk49 { get; set; }
        public uint HasAudioTracks { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }

        public Dat151Collision(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Collision;
            TypeID = (byte)Type;
        }
        public Dat151Collision(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32();
            Unk01 = br.ReadUInt32();
            Unk02 = br.ReadUInt32();
            Unk03 = br.ReadUInt32();
            Unk04 = br.ReadUInt32();
            Unk05 = br.ReadUInt32();
            Unk06 = br.ReadUInt32();
            Unk07 = br.ReadUInt32();
            Unk08 = br.ReadSingle();
            Unk09 = br.ReadUInt32();
            Unk10 = br.ReadUInt32();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadUInt32();
            Unk13 = br.ReadUInt32();
            Unk14 = br.ReadUInt32();
            Unk15 = br.ReadUInt32();
            Unk16 = br.ReadUInt32();
            Unk17 = br.ReadUInt32();
            Unk18 = br.ReadUInt32();
            Unk19 = br.ReadUInt32();
            Unk20 = br.ReadUInt32();
            Unk21 = br.ReadSingle();
            Unk22 = br.ReadSingle();
            Unk23 = br.ReadSingle();
            Unk24 = br.ReadSingle();
            Unk25 = br.ReadSingle();
            Unk26 = br.ReadSingle();
            Unk27 = br.ReadUInt32();
            Unk28 = br.ReadUInt32();
            Unk29 = br.ReadUInt32();
            Unk30 = br.ReadUInt32();
            Unk31 = br.ReadUInt32();
            Unk32 = br.ReadUInt32();
            Unk33 = br.ReadUInt32();
            Unk34 = br.ReadSingle();
            Unk35 = br.ReadSingle();
            Unk36 = br.ReadSingle();
            Unk37 = br.ReadSingle();
            Unk38 = br.ReadUInt32();
            Unk39 = br.ReadSingle();
            Unk40 = br.ReadSingle();
            Unk41 = br.ReadSingle();
            Unk42 = br.ReadSingle();
            Unk43 = br.ReadSingle();
            Unk44 = br.ReadSingle();
            Unk45 = br.ReadUInt32();
            Unk46 = br.ReadUInt32();
            Unk47 = br.ReadUInt32();
            Unk48 = br.ReadUInt32();
            Unk49 = br.ReadUInt32();
            HasAudioTracks = br.ReadUInt32();
            if (HasAudioTracks > 0)
            {
                AudioTrack0 = br.ReadUInt32();
                AudioTrack1 = br.ReadUInt32();
            }
            switch (HasAudioTracks)
            {
                case 0:
                case 1:
                    break;
                default:
                    break;
            }

            if (Unk00 != 0)
            { }
            if (Unk01 != 0)
            { }
            if (Unk38 != 0)
            { }


            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk00);
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(Unk16);
            bw.Write(Unk17);
            bw.Write(Unk18);
            bw.Write(Unk19);
            bw.Write(Unk20);
            bw.Write(Unk21);
            bw.Write(Unk22);
            bw.Write(Unk23);
            bw.Write(Unk24);
            bw.Write(Unk25);
            bw.Write(Unk26);
            bw.Write(Unk27);
            bw.Write(Unk28);
            bw.Write(Unk29);
            bw.Write(Unk30);
            bw.Write(Unk31);
            bw.Write(Unk32);
            bw.Write(Unk33);
            bw.Write(Unk34);
            bw.Write(Unk35);
            bw.Write(Unk36);
            bw.Write(Unk37);
            bw.Write(Unk38);
            bw.Write(Unk39);
            bw.Write(Unk40);
            bw.Write(Unk41);
            bw.Write(Unk42);
            bw.Write(Unk43);
            bw.Write(Unk44);
            bw.Write(Unk45);
            bw.Write(Unk46);
            bw.Write(Unk47);
            bw.Write(Unk48);
            bw.Write(Unk49);
            bw.Write(HasAudioTracks);
            if (HasAudioTracks > 0)
            {
                bw.Write(AudioTrack0);
                bw.Write(AudioTrack1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            RelXml.ValueTag(sb, indent, "Unk01", Unk01.ToString());
            RelXml.StringTag(sb, indent, "Unk02", RelXml.HashString(Unk02));
            RelXml.StringTag(sb, indent, "Unk03", RelXml.HashString(Unk03));
            RelXml.StringTag(sb, indent, "Unk04", RelXml.HashString(Unk04));
            RelXml.StringTag(sb, indent, "Unk05", RelXml.HashString(Unk05));
            RelXml.StringTag(sb, indent, "Unk06", RelXml.HashString(Unk06));
            RelXml.StringTag(sb, indent, "Unk07", RelXml.HashString(Unk07));
            RelXml.ValueTag(sb, indent, "Unk08", FloatUtil.ToString(Unk08));
            RelXml.StringTag(sb, indent, "Unk09", RelXml.HashString(Unk09));
            RelXml.StringTag(sb, indent, "Unk10", RelXml.HashString(Unk10));
            RelXml.StringTag(sb, indent, "Unk11", RelXml.HashString(Unk11));
            RelXml.StringTag(sb, indent, "Unk12", RelXml.HashString(Unk12));
            RelXml.StringTag(sb, indent, "Unk13", RelXml.HashString(Unk13));
            RelXml.StringTag(sb, indent, "Unk14", RelXml.HashString(Unk14));
            RelXml.StringTag(sb, indent, "Unk15", RelXml.HashString(Unk15));
            RelXml.StringTag(sb, indent, "Unk16", RelXml.HashString(Unk16));
            RelXml.StringTag(sb, indent, "Unk17", RelXml.HashString(Unk17));
            RelXml.StringTag(sb, indent, "Unk18", RelXml.HashString(Unk18));
            RelXml.StringTag(sb, indent, "Unk19", RelXml.HashString(Unk19));
            RelXml.StringTag(sb, indent, "Unk20", RelXml.HashString(Unk20));
            RelXml.ValueTag(sb, indent, "Unk21", FloatUtil.ToString(Unk21));
            RelXml.ValueTag(sb, indent, "Unk22", FloatUtil.ToString(Unk22));
            RelXml.ValueTag(sb, indent, "Unk23", FloatUtil.ToString(Unk23));
            RelXml.ValueTag(sb, indent, "Unk24", FloatUtil.ToString(Unk24));
            RelXml.ValueTag(sb, indent, "Unk25", FloatUtil.ToString(Unk25));
            RelXml.ValueTag(sb, indent, "Unk26", FloatUtil.ToString(Unk26));
            RelXml.StringTag(sb, indent, "Unk27", RelXml.HashString(Unk27));
            RelXml.StringTag(sb, indent, "Unk28", RelXml.HashString(Unk28));
            RelXml.StringTag(sb, indent, "Unk29", RelXml.HashString(Unk29));
            RelXml.StringTag(sb, indent, "Unk30", RelXml.HashString(Unk30));
            RelXml.StringTag(sb, indent, "Unk31", RelXml.HashString(Unk31));
            RelXml.StringTag(sb, indent, "Unk32", RelXml.HashString(Unk32));
            RelXml.StringTag(sb, indent, "Unk33", RelXml.HashString(Unk33));
            RelXml.ValueTag(sb, indent, "Unk34", FloatUtil.ToString(Unk34));
            RelXml.ValueTag(sb, indent, "Unk35", FloatUtil.ToString(Unk35));
            RelXml.ValueTag(sb, indent, "Unk36", FloatUtil.ToString(Unk36));
            RelXml.ValueTag(sb, indent, "Unk37", FloatUtil.ToString(Unk37));
            RelXml.ValueTag(sb, indent, "Unk38", Unk38.ToString());
            RelXml.ValueTag(sb, indent, "Unk39", FloatUtil.ToString(Unk39));
            RelXml.ValueTag(sb, indent, "Unk40", FloatUtil.ToString(Unk40));
            RelXml.ValueTag(sb, indent, "Unk41", FloatUtil.ToString(Unk41));
            RelXml.ValueTag(sb, indent, "Unk42", FloatUtil.ToString(Unk42));
            RelXml.ValueTag(sb, indent, "Unk43", FloatUtil.ToString(Unk43));
            RelXml.ValueTag(sb, indent, "Unk44", FloatUtil.ToString(Unk44));
            RelXml.StringTag(sb, indent, "Unk45", RelXml.HashString(Unk45));
            RelXml.StringTag(sb, indent, "Unk46", RelXml.HashString(Unk46));
            RelXml.StringTag(sb, indent, "Unk47", RelXml.HashString(Unk47));
            RelXml.StringTag(sb, indent, "Unk48", RelXml.HashString(Unk48));
            RelXml.StringTag(sb, indent, "Unk49", RelXml.HashString(Unk49));
            //RelXml.ValueTag(sb, indent, "HasAudioTracks", HasAudioTracks.ToString());
            if (HasAudioTracks > 0)
            {
                RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
                RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            Unk01 = Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk02"));
            Unk03 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk03"));
            Unk04 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk04"));
            Unk05 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk05"));
            Unk06 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk06"));
            Unk07 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk07"));
            Unk08 = Xml.GetChildFloatAttribute(node, "Unk08", "value");
            Unk09 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk09"));
            Unk10 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk10"));
            Unk11 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk11"));
            Unk12 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk12"));
            Unk13 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk13"));
            Unk14 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk14"));
            Unk15 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk15"));
            Unk16 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk16"));
            Unk17 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk17"));
            Unk18 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk18"));
            Unk19 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk19"));
            Unk20 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk20"));
            Unk21 = Xml.GetChildFloatAttribute(node, "Unk21", "value");
            Unk22 = Xml.GetChildFloatAttribute(node, "Unk22", "value");
            Unk23 = Xml.GetChildFloatAttribute(node, "Unk23", "value");
            Unk24 = Xml.GetChildFloatAttribute(node, "Unk24", "value");
            Unk25 = Xml.GetChildFloatAttribute(node, "Unk25", "value");
            Unk26 = Xml.GetChildFloatAttribute(node, "Unk26", "value");
            Unk27 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk27"));
            Unk28 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk28"));
            Unk29 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk29"));
            Unk30 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk30"));
            Unk31 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk31"));
            Unk32 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk32"));
            Unk33 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk33"));
            Unk34 = Xml.GetChildFloatAttribute(node, "Unk34", "value");
            Unk35 = Xml.GetChildFloatAttribute(node, "Unk35", "value");
            Unk36 = Xml.GetChildFloatAttribute(node, "Unk36", "value");
            Unk37 = Xml.GetChildFloatAttribute(node, "Unk37", "value");
            Unk38 = Xml.GetChildUIntAttribute(node, "Unk38", "value");
            Unk39 = Xml.GetChildFloatAttribute(node, "Unk39", "value");
            Unk40 = Xml.GetChildFloatAttribute(node, "Unk40", "value");
            Unk41 = Xml.GetChildFloatAttribute(node, "Unk41", "value");
            Unk42 = Xml.GetChildFloatAttribute(node, "Unk42", "value");
            Unk43 = Xml.GetChildFloatAttribute(node, "Unk43", "value");
            Unk44 = Xml.GetChildFloatAttribute(node, "Unk44", "value");
            Unk45 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk45"));
            Unk46 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk46"));
            Unk47 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk47"));
            Unk48 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk48"));
            Unk49 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk49"));
            if (node.SelectSingleNode("AudioTrack0") != null)
            {
                HasAudioTracks = 1;
                AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
                AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            if (HasAudioTracks > 0) return new uint[] { 204, 208 };
            else return null;
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk47 : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public float Unk1 { get; set; }

        public Dat151Unk47(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk47;
            TypeID = (byte)Type;
        }
        public Dat151Unk47(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            AudioTrack0 = br.ReadUInt32();
            Unk1 = br.ReadSingle();

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(AudioTrack0);
            bw.Write(Unk1);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.ValueTag(sb, indent, "Unk1", FloatUtil.ToString(Unk1));
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            Unk1 = Xml.GetChildFloatAttribute(node, "Unk1", "value");
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 4 };
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk83 : Dat151RelData //something to do with animals
    {
        public uint AudioItemCount { get; set; }
        public Dat151HashPair[] AudioItems { get; set; }

        public Dat151Unk83(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk83;
            TypeID = (byte)Type;
        }
        public Dat151Unk83(RelData d, BinaryReader br) : base(d, br)
        {
            AudioItemCount = br.ReadUInt32();
            var items = new Dat151HashPair[AudioItemCount];
            for (uint i = 0; i < AudioItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            AudioItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioItemCount);
            for (uint i = 0; i < AudioItemCount; i++)
            {
                bw.Write(AudioItems[i].Hash0);
                bw.Write(AudioItems[i].Hash1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (AudioItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode2 = node.SelectSingleNode("AudioItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioItemCount = (uint)vlist.Count;
                    AudioItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioItemCount; i++)
            {
                offsets.Add(8 + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151ForceRadioTrackAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk3 { get; set; }
        public MetaHash Unk4 { get; set; }
        public int Unk5 { get; set; }
        public uint AudioTracksCount { get; set; }
        public MetaHash[] AudioTracks { get; set; }

        public Dat151ForceRadioTrackAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.ForceRadioTrackAction;
            TypeID = (byte)Type;
        }
        public Dat151ForceRadioTrackAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadSingle();
            Unk4 = br.ReadUInt32();
            Unk5 = br.ReadInt32();
            AudioTracksCount = br.ReadUInt32();
            var tracks = new MetaHash[AudioTracksCount];
            for (var i = 0; i < AudioTracksCount; i++)
            {
                tracks[i] = br.ReadUInt32();
            }
            AudioTracks = tracks;

            if (Unk1 != 0)
            { }
            if (Unk2 != 0)
            { }
            if (Unk5 != 0)
            { }


            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
            bw.Write(Unk4);
            bw.Write(Unk5);
            bw.Write(AudioTracksCount);
            for (var i = 0; i < AudioTracksCount; i++)
            {
                bw.Write(AudioTracks[i]);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.StringTag(sb, indent, "Unk4", RelXml.HashString(Unk4));
            RelXml.ValueTag(sb, indent, "Unk5", Unk5.ToString());
            if (AudioTracksCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioTracks");
                var cind = indent + 1;
                for (int i = 0; i < AudioTracksCount; i++)
                {
                    var iname = RelXml.HashString(AudioTracks[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "AudioTracks");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioTracks");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            Unk4 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk4"));
            Unk5 = Xml.GetChildIntAttribute(node, "Unk5", "value");
            var vnode = node.SelectSingleNode("AudioTracks");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    AudioTracksCount = (uint)vlist.Count;
                    AudioTracks = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            offsets.Add(12);
            offsets.Add(16);
            for (uint i = 0; i < AudioTracksCount; i++)
            {
                offsets.Add(36 + i * 4);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151RadioDjSpeechAction : Dat151RelData
    {
        public FlagsUint Unk0 { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public MetaHash AudioTrack0 { get; set; }
        public MetaHash AudioTrack1 { get; set; }
        public float Unk3 { get; set; }
        public MetaHash Unk4 { get; set; }
        public int Unk5 { get; set; }

        public Dat151RadioDjSpeechAction(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.RadioDjSpeechAction;
            TypeID = (byte)Type;
        }
        public Dat151RadioDjSpeechAction(RelData d, BinaryReader br) : base(d, br)
        {
            Unk0 = br.ReadUInt32();
            Unk1 = br.ReadInt32();
            Unk2 = br.ReadInt32();
            AudioTrack0 = br.ReadUInt32();
            AudioTrack1 = br.ReadUInt32();
            Unk3 = br.ReadSingle();
            Unk4 = br.ReadUInt32();
            Unk5 = br.ReadInt32();

            if (Unk1 != 0)
            { }
            if (Unk2 != 0)
            { }
            if (Unk3 != 0)
            { }
            if (Unk4 != 0)
            { }
            if (Unk5 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk0);
            bw.Write(Unk1);
            bw.Write(Unk2);
            bw.Write(AudioTrack0);
            bw.Write(AudioTrack1);
            bw.Write(Unk3);
            bw.Write(Unk4);
            bw.Write(Unk5);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk0", "0x" + Unk0.Hex);
            RelXml.ValueTag(sb, indent, "Unk1", Unk1.ToString());
            RelXml.ValueTag(sb, indent, "Unk2", Unk2.ToString());
            RelXml.StringTag(sb, indent, "AudioTrack0", RelXml.HashString(AudioTrack0));
            RelXml.StringTag(sb, indent, "AudioTrack1", RelXml.HashString(AudioTrack1));
            RelXml.ValueTag(sb, indent, "Unk3", FloatUtil.ToString(Unk3));
            RelXml.StringTag(sb, indent, "Unk4", RelXml.HashString(Unk4));
            RelXml.ValueTag(sb, indent, "Unk5", Unk5.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            Unk0 = Xml.GetChildUIntAttribute(node, "Unk0", "value");
            Unk1 = Xml.GetChildIntAttribute(node, "Unk1", "value");
            Unk2 = Xml.GetChildIntAttribute(node, "Unk2", "value");
            AudioTrack0 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack0"));
            AudioTrack1 = XmlRel.GetHash(Xml.GetChildInnerText(node, "AudioTrack1"));
            Unk3 = Xml.GetChildFloatAttribute(node, "Unk3", "value");
            Unk4 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk4"));
            Unk5 = Xml.GetChildIntAttribute(node, "Unk5", "value");
        }
        public override uint[] GetHashTableOffsets()
        {
            return new uint[] { 12, 16 };
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk78 : Dat151RelData
    {
        public uint AudioItemCount { get; set; }
        public Dat151HashPair[] AudioItems { get; set; }

        public Dat151Unk78(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk78;
            TypeID = (byte)Type;
        }
        public Dat151Unk78(RelData d, BinaryReader br) : base(d, br)
        {
            AudioItemCount = br.ReadUInt32();
            var items = new Dat151HashPair[AudioItemCount];
            for (var i = 0; i < AudioItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            AudioItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioItemCount);
            for (var i = 0; i < AudioItemCount; i++)
            {
                bw.Write(AudioItems[i].Hash0);
                bw.Write(AudioItems[i].Hash1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (AudioItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode2 = node.SelectSingleNode("AudioItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioItemCount = (uint)vlist.Count;
                    AudioItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioItemCount; i++)
            {
                offsets.Add(8 + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk49 : Dat151RelData //doors/gates?
    {
        public uint AudioItemCount { get; set; }
        public Dat151HashPair[] AudioItems { get; set; }

        public Dat151Unk49(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk49;
            TypeID = (byte)Type;
        }
        public Dat151Unk49(RelData d, BinaryReader br) : base(d, br)
        {
            AudioItemCount = br.ReadUInt32();
            var items = new Dat151HashPair[AudioItemCount];
            for (var i = 0; i < AudioItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            AudioItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioItemCount);
            for (var i = 0; i < AudioItemCount; i++)
            {
                bw.Write(AudioItems[i].Hash0);
                bw.Write(AudioItems[i].Hash1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (AudioItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode2 = node.SelectSingleNode("AudioItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioItemCount = (uint)vlist.Count;
                    AudioItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioItemCount; i++)
            {
                offsets.Add(8 + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk84 : Dat151RelData
    {
        public uint AudioItemCount { get; set; }
        public Dat151HashPair[] AudioItems { get; set; }

        public Dat151Unk84(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk84;
            TypeID = (byte)Type;
        }
        public Dat151Unk84(RelData d, BinaryReader br) : base(d, br)
        {
            AudioItemCount = br.ReadUInt32();
            var items = new Dat151HashPair[AudioItemCount];
            for (var i = 0; i < AudioItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            AudioItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioItemCount);
            for (var i = 0; i < AudioItemCount; i++)
            {
                bw.Write(AudioItems[i].Hash0);
                bw.Write(AudioItems[i].Hash1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (AudioItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode2 = node.SelectSingleNode("AudioItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioItemCount = (uint)vlist.Count;
                    AudioItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioItemCount; i++)
            {
                offsets.Add(8 + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk86 : Dat151RelData
    {
        public uint AudioItemCount { get; set; }
        public Dat151HashPair[] AudioItems { get; set; }

        public Dat151Unk86(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk86;
            TypeID = (byte)Type;
        }
        public Dat151Unk86(RelData d, BinaryReader br) : base(d, br)
        {
            AudioItemCount = br.ReadUInt32();
            var items = new Dat151HashPair[AudioItemCount];
            for (var i = 0; i < AudioItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            AudioItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioItemCount);
            for (var i = 0; i < AudioItemCount; i++)
            {
                bw.Write(AudioItems[i].Hash0);
                bw.Write(AudioItems[i].Hash1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (AudioItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode2 = node.SelectSingleNode("AudioItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioItemCount = (uint)vlist.Count;
                    AudioItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioItemCount; i++)
            {
                offsets.Add(8 + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk81 : Dat151RelData
    {
        public uint AudioItemCount { get; set; }
        public Dat151HashPair[] AudioItems { get; set; }

        public Dat151Unk81(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk81;
            TypeID = (byte)Type;
        }
        public Dat151Unk81(RelData d, BinaryReader br) : base(d, br)
        {
            AudioItemCount = br.ReadUInt32();
            var items = new Dat151HashPair[AudioItemCount];
            for (var i = 0; i < AudioItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            AudioItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioItemCount);
            for (var i = 0; i < AudioItemCount; i++)
            {
                bw.Write(AudioItems[i].Hash0);
                bw.Write(AudioItems[i].Hash1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (AudioItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode2 = node.SelectSingleNode("AudioItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioItemCount = (uint)vlist.Count;
                    AudioItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioItemCount; i++)
            {
                offsets.Add(8 + i * 8);
            }
            return offsets.ToArray();
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk55 : Dat151RelData
    {
        public uint AudioItemCount { get; set; }
        public Dat151HashPair[] AudioItems { get; set; }

        public Dat151Unk55(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk55;
            TypeID = (byte)Type;
        }
        public Dat151Unk55(RelData d, BinaryReader br) : base(d, br)
        {
            AudioItemCount = br.ReadUInt32();
            var items = new Dat151HashPair[AudioItemCount];
            for (var i = 0; i < AudioItemCount; i++)
            {
                items[i] = new Dat151HashPair(br.ReadUInt32(), br.ReadUInt32());
            }
            AudioItems = items;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(AudioItemCount);
            for (var i = 0; i < AudioItemCount; i++)
            {
                bw.Write(AudioItems[i].Hash0);
                bw.Write(AudioItems[i].Hash1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (AudioItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "AudioItems");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < AudioItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    AudioItems[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "AudioItems");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "AudioItems");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode2 = node.SelectSingleNode("AudioItems");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Dat151HashPair>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Dat151HashPair();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    AudioItemCount = (uint)vlist.Count;
                    AudioItems = vlist.ToArray();
                }
            }
        }
        public override uint[] GetHashTableOffsets()
        {
            var offsets = new List<uint>();
            for (uint i = 0; i < AudioItemCount; i++)
            {
                offsets.Add(8 + i * 8);
            }
            return offsets.ToArray();
        }
    }

    [TC(typeof(EXP))] public class Dat151ShoreLinePool : Dat151RelData
    {
        public FlagsUint Unk01 { get; set; }
        public Vector4 Unk02 { get; set; }
        public int Unk03 { get; set; }
        public int Unk04 { get; set; }
        public int Unk05 { get; set; }
        public int Unk06 { get; set; }
        public int Unk07 { get; set; }
        public int Unk08 { get; set; }
        public int Unk09 { get; set; }
        public int Unk10 { get; set; }
        public int Unk11 { get; set; }
        public float Unk12 { get; set; }
        public int PointsCount { get; set; }
        public Vector2[] Points { get; set; }


        public Dat151ShoreLinePool(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.ShoreLinePool;
            TypeID = (byte)Type;
        }
        public Dat151ShoreLinePool(RelData d, BinaryReader br) : base(d, br)
        {
            Unk01 = br.ReadUInt32();
            Unk02 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unk03 = br.ReadInt32();
            Unk04 = br.ReadInt32();
            Unk05 = br.ReadInt32();
            Unk06 = br.ReadInt32();
            Unk07 = br.ReadInt32();
            Unk08 = br.ReadInt32();
            Unk09 = br.ReadInt32();
            Unk10 = br.ReadInt32();
            Unk11 = br.ReadInt32();
            Unk12 = br.ReadSingle();

            PointsCount = br.ReadInt32();
            var points = new Vector2[PointsCount];
            for (int i = 0; i < PointsCount; i++)
            {
                points[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            }
            Points = points;

            //switch (Unk12)
            //{
            //    case 4.267251f:
            //    case 2.055879f:
            //        break;
            //    default:
            //        break;
            //}

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk01);
            bw.Write(Unk02.X);
            bw.Write(Unk02.Y);
            bw.Write(Unk02.Z);
            bw.Write(Unk02.W);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);

            bw.Write(PointsCount);
            for (int i = 0; i < PointsCount; i++)
            {
                bw.Write(Points[i].X);
                bw.Write(Points[i].Y);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk01", "0x" + Unk01.Hex);
            RelXml.SelfClosingTag(sb, indent, "Unk02 " + FloatUtil.GetVector4XmlString(Unk02));
            RelXml.ValueTag(sb, indent, "Unk03", Unk03.ToString());
            RelXml.ValueTag(sb, indent, "Unk04", Unk04.ToString());
            RelXml.ValueTag(sb, indent, "Unk05", Unk05.ToString());
            RelXml.ValueTag(sb, indent, "Unk06", Unk06.ToString());
            RelXml.ValueTag(sb, indent, "Unk07", Unk07.ToString());
            RelXml.ValueTag(sb, indent, "Unk08", Unk08.ToString());
            RelXml.ValueTag(sb, indent, "Unk09", Unk09.ToString());
            RelXml.ValueTag(sb, indent, "Unk10", Unk10.ToString());
            RelXml.ValueTag(sb, indent, "Unk11", Unk11.ToString());
            RelXml.ValueTag(sb, indent, "Unk12", FloatUtil.ToString(Unk12));
            //RelXml.StringTag(sb, indent, "Unk12", RelXml.HashString(Unk12));
            RelXml.WriteRawArray(sb, Points, indent, "Points", "", RelXml.FormatVector2, 1);
        }
        public override void ReadXml(XmlNode node)
        {
            Unk01 = Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = Xml.GetChildVector4Attributes(node, "Unk02", "x", "y", "z", "w");
            Unk03 = Xml.GetChildIntAttribute(node, "Unk03", "value");
            Unk04 = Xml.GetChildIntAttribute(node, "Unk04", "value");
            Unk05 = Xml.GetChildIntAttribute(node, "Unk05", "value");
            Unk06 = Xml.GetChildIntAttribute(node, "Unk06", "value");
            Unk07 = Xml.GetChildIntAttribute(node, "Unk07", "value");
            Unk08 = Xml.GetChildIntAttribute(node, "Unk08", "value");
            Unk09 = Xml.GetChildIntAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildIntAttribute(node, "Unk10", "value");
            Unk11 = Xml.GetChildIntAttribute(node, "Unk11", "value");
            Unk12 = Xml.GetChildFloatAttribute(node, "Unk12", "value");
            //Unk12 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk12"));
            Points = Xml.GetChildRawVector2Array(node, "Points");
            PointsCount = Points?.Length ?? 0;
        }
    }
    [TC(typeof(EXP))] public class Dat151ShoreLineLake : Dat151RelData
    {
        public FlagsUint Unk01 { get; set; }
        public Vector4 Unk02 { get; set; }
        public int Unk03 { get; set; }
        public int Unk04 { get; set; }
        public uint Unk05 { get; set; }
        public int PointsCount { get; set; }
        public Vector2[] Points { get; set; }

        public Dat151ShoreLineLake(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.ShoreLineLake;
            TypeID = (byte)Type;
        }
        public Dat151ShoreLineLake(RelData d, BinaryReader br) : base(d, br)
        {
            Unk01 = br.ReadUInt32();
            Unk02 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unk03 = br.ReadInt32();
            Unk04 = br.ReadInt32();
            Unk05 = br.ReadUInt32();

            byte b1 = (byte)((Unk05) & 0xFF);
            byte b2 = (byte)((Unk05>>8) & 0xFF);
            PointsCount = b2;

            var points = new Vector2[PointsCount];
            for (int i = 0; i < PointsCount; i++)
            {
                points[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            }
            Points = points;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk01);
            bw.Write(Unk02.X);
            bw.Write(Unk02.Y);
            bw.Write(Unk02.Z);
            bw.Write(Unk02.W);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);

            //byte b1 = (byte)((Unk05) & 0xFF);
            //byte b2 = (byte)((Unk05 >> 8) & 0xFF);
            //PointsCount = b2;

            for (int i = 0; i < PointsCount; i++)
            {
                bw.Write(Points[i].X);
                bw.Write(Points[i].Y);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk01", "0x" + Unk01.Hex);
            RelXml.SelfClosingTag(sb, indent, "Unk02 " + FloatUtil.GetVector4XmlString(Unk02));
            RelXml.ValueTag(sb, indent, "Unk03", Unk03.ToString());
            RelXml.ValueTag(sb, indent, "Unk04", Unk04.ToString());
            RelXml.ValueTag(sb, indent, "Unk05", Unk05.ToString());
            RelXml.WriteRawArray(sb, Points, indent, "Points", "", RelXml.FormatVector2, 1);
        }
        public override void ReadXml(XmlNode node)
        {
            Unk01 = Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = Xml.GetChildVector4Attributes(node, "Unk02", "x", "y", "z", "w");
            Unk03 = Xml.GetChildIntAttribute(node, "Unk03", "value");
            Unk04 = Xml.GetChildIntAttribute(node, "Unk04", "value");
            Unk05 = Xml.GetChildUIntAttribute(node, "Unk05", "value");
            Points = Xml.GetChildRawVector2Array(node, "Points");
            PointsCount = Points?.Length ?? 0;
        }
    }
    [TC(typeof(EXP))] public class Dat151ShoreLineRiver : Dat151RelData
    {
        public FlagsUint Unk01 { get; set; }
        public Vector4 Unk02 { get; set; }
        public float Unk03 { get; set; }
        public uint Unk04 { get; set; }
        public uint Unk05 { get; set; }
        public uint Unk06 { get; set; }
        public uint PointsCount { get; set; }
        public Vector3[] Points { get; set; }

        public Dat151ShoreLineRiver(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.ShoreLineRiver;
            TypeID = (byte)Type;
        }
        public Dat151ShoreLineRiver(RelData d, BinaryReader br) : base(d, br)
        {
            Unk01 = br.ReadUInt32();
            Unk02 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unk03 = br.ReadSingle();
            Unk04 = br.ReadUInt32();
            Unk05 = br.ReadUInt32();
            Unk06 = br.ReadUInt32();
            PointsCount = br.ReadUInt32();

            var points = new Vector3[PointsCount];
            for (int i = 0; i < PointsCount; i++)
            {
                points[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            }
            Points = points;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk01);
            bw.Write(Unk02.X);
            bw.Write(Unk02.Y);
            bw.Write(Unk02.Z);
            bw.Write(Unk02.W);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(PointsCount);

            for (int i = 0; i < PointsCount; i++)
            {
                bw.Write(Points[i].X);
                bw.Write(Points[i].Y);
                bw.Write(Points[i].Z);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk01", "0x" + Unk01.Hex);
            RelXml.SelfClosingTag(sb, indent, "Unk02 " + FloatUtil.GetVector4XmlString(Unk02));
            RelXml.ValueTag(sb, indent, "Unk03", FloatUtil.ToString(Unk03));
            RelXml.ValueTag(sb, indent, "Unk04", Unk04.ToString());
            RelXml.ValueTag(sb, indent, "Unk05", Unk05.ToString());
            RelXml.ValueTag(sb, indent, "Unk06", Unk06.ToString());
            RelXml.WriteRawArray(sb, Points, indent, "Points", "", RelXml.FormatVector3, 1);
        }
        public override void ReadXml(XmlNode node)
        {
            Unk01 = Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = Xml.GetChildVector4Attributes(node, "Unk02", "x", "y", "z", "w");
            Unk03 = Xml.GetChildFloatAttribute(node, "Unk03", "value");
            Unk04 = Xml.GetChildUIntAttribute(node, "Unk04", "value");
            Unk05 = Xml.GetChildUIntAttribute(node, "Unk05", "value");
            Unk06 = Xml.GetChildUIntAttribute(node, "Unk06", "value");
            Points = Xml.GetChildRawVector3Array(node, "Points");
            PointsCount = (uint)(Points?.Length ?? 0);
        }
    }
    [TC(typeof(EXP))] public class Dat151ShoreLineOcean : Dat151RelData
    {
        public FlagsUint Unk01 { get; set; }
        public Vector4 Unk02 { get; set; }
        public float Unk03 { get; set; }
        public uint Unk04 { get; set; }
        public MetaHash Unk05 { get; set; }
        public float Unk06 { get; set; }
        public float Unk07 { get; set; }
        public float Unk08 { get; set; }
        public float Unk09 { get; set; }
        public float Unk10 { get; set; }
        public float Unk11 { get; set; }
        public float Unk12 { get; set; }
        public uint PointsCount { get; set; }
        public Vector2[] Points { get; set; }

        public Dat151ShoreLineOcean(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.ShoreLineOcean;
            TypeID = (byte)Type;
        }
        public Dat151ShoreLineOcean(RelData d, BinaryReader br) : base(d, br)
        {
            Unk01 = br.ReadUInt32();
            Unk02 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Unk03 = br.ReadSingle();
            Unk04 = br.ReadUInt32();
            Unk05 = br.ReadUInt32();
            Unk06 = br.ReadSingle();
            Unk07 = br.ReadSingle();
            Unk08 = br.ReadSingle();
            Unk09 = br.ReadSingle();
            Unk10 = br.ReadSingle();
            Unk11 = br.ReadSingle();
            Unk12 = br.ReadSingle();

            PointsCount = br.ReadUInt32();

            var points = new Vector2[PointsCount];
            for (int i = 0; i < PointsCount; i++)
            {
                points[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            }
            Points = points;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk01);
            bw.Write(Unk02.X);
            bw.Write(Unk02.Y);
            bw.Write(Unk02.Z);
            bw.Write(Unk02.W);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);

            bw.Write(PointsCount);

            for (int i = 0; i < PointsCount; i++)
            {
                bw.Write(Points[i].X);
                bw.Write(Points[i].Y);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk01", "0x" + Unk01.Hex);
            RelXml.SelfClosingTag(sb, indent, "Unk02 " + FloatUtil.GetVector4XmlString(Unk02));
            RelXml.ValueTag(sb, indent, "Unk03", FloatUtil.ToString(Unk03));
            RelXml.ValueTag(sb, indent, "Unk04", Unk04.ToString());
            RelXml.StringTag(sb, indent, "Unk05", RelXml.HashString(Unk05));
            RelXml.ValueTag(sb, indent, "Unk06", FloatUtil.ToString(Unk06));
            RelXml.ValueTag(sb, indent, "Unk07", FloatUtil.ToString(Unk07));
            RelXml.ValueTag(sb, indent, "Unk08", FloatUtil.ToString(Unk08));
            RelXml.ValueTag(sb, indent, "Unk09", FloatUtil.ToString(Unk09));
            RelXml.ValueTag(sb, indent, "Unk10", FloatUtil.ToString(Unk10));
            RelXml.ValueTag(sb, indent, "Unk11", FloatUtil.ToString(Unk11));
            RelXml.ValueTag(sb, indent, "Unk12", FloatUtil.ToString(Unk12));
            RelXml.WriteRawArray(sb, Points, indent, "Points", "", RelXml.FormatVector2, 1);
        }
        public override void ReadXml(XmlNode node)
        {
            Unk01 = Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = Xml.GetChildVector4Attributes(node, "Unk02", "x", "y", "z", "w");
            Unk03 = Xml.GetChildFloatAttribute(node, "Unk03", "value");
            Unk04 = Xml.GetChildUIntAttribute(node, "Unk04", "value");
            Unk05 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk05"));
            Unk06 = Xml.GetChildFloatAttribute(node, "Unk06", "value");
            Unk07 = Xml.GetChildFloatAttribute(node, "Unk07", "value");
            Unk08 = Xml.GetChildFloatAttribute(node, "Unk08", "value");
            Unk09 = Xml.GetChildFloatAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildFloatAttribute(node, "Unk10", "value");
            Unk11 = Xml.GetChildFloatAttribute(node, "Unk11", "value");
            Unk12 = Xml.GetChildFloatAttribute(node, "Unk12", "value");
            Points = Xml.GetChildRawVector2Array(node, "Points");
            PointsCount = (uint)(Points?.Length ?? 0);
        }
    }
    [TC(typeof(EXP))] public class Dat151ShoreLineList : Dat151RelData
    {
        public uint ShoreLineCount { get; set; }
        public MetaHash[] ShoreLines { get; set; }

        public Dat151ShoreLineList(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.ShoreLineList;
            TypeID = (byte)Type;
        }
        public Dat151ShoreLineList(RelData d, BinaryReader br) : base(d, br)
        {
            ShoreLineCount = br.ReadUInt32();
            var shorelines = new MetaHash[ShoreLineCount];
            for (int i = 0; i < ShoreLineCount; i++)
            {
                shorelines[i] = br.ReadUInt32();
            }
            ShoreLines = shorelines;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(ShoreLineCount);
            for (int i = 0; i < ShoreLineCount; i++)
            {
                bw.Write(ShoreLines[i]);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (ShoreLineCount > 0)
            {
                RelXml.OpenTag(sb, indent, "ShoreLines");
                var cind = indent + 1;
                for (int i = 0; i < ShoreLineCount; i++)
                {
                    var iname = RelXml.HashString(ShoreLines[i]);
                    RelXml.StringTag(sb, cind, "Item", iname);
                }
                RelXml.CloseTag(sb, indent, "ShoreLines");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "ShoreLines");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode = node.SelectSingleNode("ShoreLines");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<MetaHash>();
                    foreach (XmlNode inode in inodes)
                    {
                        vlist.Add(XmlRel.GetHash(inode.InnerText));
                    }
                    ShoreLineCount = (uint)vlist.Count;
                    ShoreLines = vlist.ToArray();
                }
            }
        }
    }


    [TC(typeof(EXP))] public class Dat151Unk114 : Dat151RelData
    {
        public uint ItemCount { get; set; }
        public ItemValue[] Items { get; set; }

        public struct ItemValue
        {
            public uint Unk0 { get; set; }
            public uint Unk1 { get; set; }

            public ItemValue(uint unk0, uint unk1)
            {
                Unk0 = unk0;
                Unk1 = unk1;
            }

            public override string ToString()
            {
                return Unk0.ToString() + ": " + Unk1.ToString();
            }
        }

        public Dat151Unk114(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk114;
            TypeID = (byte)Type;
        }
        public Dat151Unk114(RelData d, BinaryReader br) : base(d, br)
        {
            ItemCount = br.ReadUInt32();
            Items = new ItemValue[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                Items[i] = new ItemValue(br.ReadUInt32(), br.ReadUInt32());
            }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(ItemCount);
            for (int i = 0; i < ItemCount; i++)
            {
                bw.Write(Items[i].Unk0);
                bw.Write(Items[i].Unk1);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            if (ItemCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Items");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < ItemCount; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    RelXml.ValueTag(sb, cind2, "Unk0", Items[i].Unk0.ToString());
                    RelXml.ValueTag(sb, cind2, "Unk1", Items[i].Unk1.ToString());
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Items");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            var vnode = node.SelectSingleNode("Items");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<ItemValue>();
                    foreach (XmlNode inode in inodes)
                    {
                        ItemValue v = new ItemValue();
                        v.Unk0 = Xml.GetChildUIntAttribute(inode, "Unk0", "value");
                        v.Unk1 = Xml.GetChildUIntAttribute(inode, "Unk1", "value");
                        vlist.Add(v);
                    }
                    ItemCount = (uint)vlist.Count;
                    Items = vlist.ToArray();
                }
            }
        }
    }
    [TC(typeof(EXP))] public class Dat151VehicleEngineGranular : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; }
        public int Unk01 { get; set; }
        public MetaHash Unk02 { get; set; }
        public MetaHash Unk03 { get; set; }
        public int Unk04 { get; set; }
        public int Unk05 { get; set; }
        public int Unk06 { get; set; }
        public int Unk07 { get; set; }
        public int Unk08 { get; set; }
        public int Unk09 { get; set; }
        public int Unk10 { get; set; }
        public int Unk11 { get; set; }
        public int Unk12 { get; set; }
        public int Unk13 { get; set; }
        public int Unk14 { get; set; }
        public int Unk15 { get; set; }
        public int Unk16 { get; set; }
        public int Unk17 { get; set; }
        public int Unk18 { get; set; }
        public int Unk19 { get; set; }
        public int Unk20 { get; set; }
        public int Unk21 { get; set; }
        public float Unk22 { get; set; }
        public float Unk23 { get; set; }
        public float Unk24 { get; set; }
        public float Unk25 { get; set; }
        public float Unk26 { get; set; }
        public float Unk27 { get; set; }
        public float Unk28 { get; set; }
        public int Unk29 { get; set; }
        public int Unk30 { get; set; }
        public MetaHash Unk31 { get; set; }
        public MetaHash Unk32 { get; set; }
        public MetaHash Unk33 { get; set; }
        public MetaHash Unk34 { get; set; }
        public MetaHash Unk35 { get; set; }
        public MetaHash Unk36 { get; set; }
        public MetaHash Unk37 { get; set; }
        public int Unk38 { get; set; }
        public int Unk39 { get; set; }
        public MetaHash Unk40 { get; set; }
        public MetaHash Unk41 { get; set; }
        public int Unk42 { get; set; }
        public int Unk43 { get; set; }
        public int Unk44 { get; set; }
        public MetaHash Unk45 { get; set; }
        public int Unk46 { get; set; }
        public int Unk47 { get; set; }
        public int Unk48 { get; set; }
        public int Unk49 { get; set; }
        public int Unk50 { get; set; }
        public int Unk51 { get; set; }
        public MetaHash Unk52 { get; set; }
        public MetaHash Unk53 { get; set; }
        public int Unk54 { get; set; }
        public int Unk55 { get; set; }
        public int Unk56 { get; set; }
        public int Unk57 { get; set; }
        public int Unk58 { get; set; }//OPTIONAL!? only include this and next if either nonzero?
        public float Unk59 { get; set; }//OPTIONAL!?

        public Dat151VehicleEngineGranular(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.VehicleEngineGranular;
            TypeID = (byte)Type;
        }
        public Dat151VehicleEngineGranular(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32();
            Unk01 = br.ReadInt32();
            Unk02 = br.ReadUInt32();
            Unk03 = br.ReadUInt32();
            Unk04 = br.ReadInt32();
            Unk05 = br.ReadInt32();
            Unk06 = br.ReadInt32();
            Unk07 = br.ReadInt32();
            Unk08 = br.ReadInt32();
            Unk09 = br.ReadInt32();
            Unk10 = br.ReadInt32();
            Unk11 = br.ReadInt32();
            Unk12 = br.ReadInt32();
            Unk13 = br.ReadInt32();
            Unk14 = br.ReadInt32();
            Unk15 = br.ReadInt32();
            Unk16 = br.ReadInt32();
            Unk17 = br.ReadInt32();
            Unk18 = br.ReadInt32();
            Unk19 = br.ReadInt32();
            Unk20 = br.ReadInt32();
            Unk21 = br.ReadInt32();
            Unk22 = br.ReadSingle();
            Unk23 = br.ReadSingle();
            Unk24 = br.ReadSingle();
            Unk25 = br.ReadSingle();
            Unk26 = br.ReadSingle();
            Unk27 = br.ReadSingle();
            Unk28 = br.ReadSingle();
            Unk29 = br.ReadInt32();
            Unk30 = br.ReadInt32();
            Unk31 = br.ReadUInt32();
            Unk32 = br.ReadUInt32();
            Unk33 = br.ReadUInt32();
            Unk34 = br.ReadUInt32();
            Unk35 = br.ReadUInt32();
            Unk36 = br.ReadUInt32();
            Unk37 = br.ReadUInt32();
            Unk38 = br.ReadInt32();
            Unk39 = br.ReadInt32();
            Unk40 = br.ReadUInt32();
            Unk41 = br.ReadUInt32();
            Unk42 = br.ReadInt32();
            Unk43 = br.ReadInt32();
            Unk44 = br.ReadInt32();
            Unk45 = br.ReadUInt32();
            Unk46 = br.ReadInt32();
            Unk47 = br.ReadInt32();
            Unk48 = br.ReadInt32();
            Unk49 = br.ReadInt32();
            Unk50 = br.ReadInt32();
            Unk51 = br.ReadInt32();
            Unk52 = br.ReadUInt32();
            Unk53 = br.ReadUInt32();
            Unk54 = br.ReadInt32();
            Unk55 = br.ReadInt32();
            Unk56 = br.ReadInt32();
            Unk57 = br.ReadInt32();



            switch (this.Unk00)
            {
                case 0xAAAAA905:
                case 0xAAAAA955:
                case 0xAAAAA954:
                case 0xAAAAA914:
                case 0xAAAAA904:
                case 0xAAAAA805:
                case 0xAAAAA915:
                case 0xAAAAA945:
                case 0xAAAAA815:
                case 0xAAAAA944:
                case 0xAAAAA854:
                    break;
                default:
                    break;
            }
            switch (this.Unk40)
            {
                case 1225003942:
                    break;
                default:
                    break;
            }
            switch (this.Unk41)
            {
                case 1479769906:
                    break;
                default:
                    break;
            }
            switch (this.Unk43)
            {
                case 5:
                case 3:
                case 4:
                case 2:
                case 6:
                case 1:
                    break;
                default:
                    break;
            }
            switch (this.Unk44)
            {
                case 2:
                case 1:
                case 3:
                case 4:
                    break;
                default:
                    break;
            }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            switch (bytesleft) //any other way to tell??
            {
                case 0:
                    break;
                case 8:
                    Unk58 = br.ReadInt32();
                    Unk59 = br.ReadSingle();
                    switch (Unk58)
                    {
                        case 1:
                        case 2:
                        case 0:
                            break;
                        default:
                            break;//no hit here
                    }
                    if ((Unk58 == 0) && (Unk59 == 0))
                    { }//no hit here
                    break;
                default:
                    break;//no hit here
            }

            if (bytesleft != 0)
            { }

            if (Unk04 != 0)
            { }
            if (Unk09 != 0)
            { }
            if (Unk10 != 0)
            { }
            if (Unk11 != 0)
            { }
            if (Unk12 != 0)
            { }
            if (Unk38 != 0)
            { }
            if (Unk39 != 0)
            { }
            if (Unk47 != 0)
            { }
            if (Unk48 != 0)
            { }
            if (Unk50 != 0)
            { }
            if (Unk51 != 0)
            { }


        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
            bw.Write(Unk00);
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(Unk16);
            bw.Write(Unk17);
            bw.Write(Unk18);
            bw.Write(Unk19);
            bw.Write(Unk20);
            bw.Write(Unk21);
            bw.Write(Unk22);
            bw.Write(Unk23);
            bw.Write(Unk24);
            bw.Write(Unk25);
            bw.Write(Unk26);
            bw.Write(Unk27);
            bw.Write(Unk28);
            bw.Write(Unk29);
            bw.Write(Unk30);
            bw.Write(Unk31);
            bw.Write(Unk32);
            bw.Write(Unk33);
            bw.Write(Unk34);
            bw.Write(Unk35);
            bw.Write(Unk36);
            bw.Write(Unk37);
            bw.Write(Unk38);
            bw.Write(Unk39);
            bw.Write(Unk40);
            bw.Write(Unk41);
            bw.Write(Unk42);
            bw.Write(Unk43);
            bw.Write(Unk44);
            bw.Write(Unk45);
            bw.Write(Unk46);
            bw.Write(Unk47);
            bw.Write(Unk48);
            bw.Write(Unk49);
            bw.Write(Unk50);
            bw.Write(Unk51);
            bw.Write(Unk52);
            bw.Write(Unk53);
            bw.Write(Unk54);
            bw.Write(Unk55);
            bw.Write(Unk56);
            bw.Write(Unk57);
            if ((Unk58 != 0) || (Unk59 != 0))//how else to know?? seems hacky!
            {
                bw.Write(Unk58);
                bw.Write(Unk59);
            }
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            RelXml.ValueTag(sb, indent, "Unk01", Unk01.ToString());
            RelXml.StringTag(sb, indent, "Unk02", RelXml.HashString(Unk02));
            RelXml.StringTag(sb, indent, "Unk03", RelXml.HashString(Unk03));
            RelXml.ValueTag(sb, indent, "Unk04", Unk04.ToString());
            RelXml.ValueTag(sb, indent, "Unk05", Unk05.ToString());
            RelXml.ValueTag(sb, indent, "Unk06", Unk06.ToString());
            RelXml.ValueTag(sb, indent, "Unk07", Unk07.ToString());
            RelXml.ValueTag(sb, indent, "Unk08", Unk08.ToString());
            RelXml.ValueTag(sb, indent, "Unk09", Unk09.ToString());
            RelXml.ValueTag(sb, indent, "Unk10", Unk10.ToString());
            RelXml.ValueTag(sb, indent, "Unk11", Unk11.ToString());
            RelXml.ValueTag(sb, indent, "Unk12", Unk12.ToString());
            RelXml.ValueTag(sb, indent, "Unk13", Unk13.ToString());
            RelXml.ValueTag(sb, indent, "Unk14", Unk14.ToString());
            RelXml.ValueTag(sb, indent, "Unk15", Unk15.ToString());
            RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
            RelXml.ValueTag(sb, indent, "Unk17", Unk17.ToString());
            RelXml.ValueTag(sb, indent, "Unk18", Unk18.ToString());
            RelXml.ValueTag(sb, indent, "Unk19", Unk19.ToString());
            RelXml.ValueTag(sb, indent, "Unk20", Unk20.ToString());
            RelXml.ValueTag(sb, indent, "Unk21", Unk21.ToString());
            RelXml.ValueTag(sb, indent, "Unk22", FloatUtil.ToString(Unk22));
            RelXml.ValueTag(sb, indent, "Unk23", FloatUtil.ToString(Unk23));
            RelXml.ValueTag(sb, indent, "Unk24", FloatUtil.ToString(Unk24));
            RelXml.ValueTag(sb, indent, "Unk25", FloatUtil.ToString(Unk25));
            RelXml.ValueTag(sb, indent, "Unk26", FloatUtil.ToString(Unk26));
            RelXml.ValueTag(sb, indent, "Unk27", FloatUtil.ToString(Unk27));
            RelXml.ValueTag(sb, indent, "Unk28", FloatUtil.ToString(Unk28));
            RelXml.ValueTag(sb, indent, "Unk29", Unk29.ToString());
            RelXml.ValueTag(sb, indent, "Unk30", Unk30.ToString());
            RelXml.StringTag(sb, indent, "Unk31", RelXml.HashString(Unk31));
            RelXml.StringTag(sb, indent, "Unk32", RelXml.HashString(Unk32));
            RelXml.StringTag(sb, indent, "Unk33", RelXml.HashString(Unk33));
            RelXml.StringTag(sb, indent, "Unk34", RelXml.HashString(Unk34));
            RelXml.StringTag(sb, indent, "Unk35", RelXml.HashString(Unk35));
            RelXml.StringTag(sb, indent, "Unk36", RelXml.HashString(Unk36));
            RelXml.StringTag(sb, indent, "Unk37", RelXml.HashString(Unk37));
            RelXml.ValueTag(sb, indent, "Unk38", Unk38.ToString());
            RelXml.ValueTag(sb, indent, "Unk39", Unk39.ToString());
            RelXml.StringTag(sb, indent, "Unk40", RelXml.HashString(Unk40));
            RelXml.StringTag(sb, indent, "Unk41", RelXml.HashString(Unk41));
            RelXml.ValueTag(sb, indent, "Unk42", Unk42.ToString());
            RelXml.ValueTag(sb, indent, "Unk43", Unk43.ToString());
            RelXml.ValueTag(sb, indent, "Unk44", Unk44.ToString());
            RelXml.StringTag(sb, indent, "Unk45", RelXml.HashString(Unk45));
            RelXml.ValueTag(sb, indent, "Unk46", Unk46.ToString());
            RelXml.ValueTag(sb, indent, "Unk47", Unk47.ToString());
            RelXml.ValueTag(sb, indent, "Unk48", Unk48.ToString());
            RelXml.ValueTag(sb, indent, "Unk49", Unk49.ToString());
            RelXml.ValueTag(sb, indent, "Unk50", Unk50.ToString());
            RelXml.ValueTag(sb, indent, "Unk51", Unk51.ToString());
            RelXml.StringTag(sb, indent, "Unk52", RelXml.HashString(Unk52));
            RelXml.StringTag(sb, indent, "Unk53", RelXml.HashString(Unk53));
            RelXml.ValueTag(sb, indent, "Unk54", Unk54.ToString());
            RelXml.ValueTag(sb, indent, "Unk55", Unk55.ToString());
            RelXml.ValueTag(sb, indent, "Unk56", Unk56.ToString());
            RelXml.ValueTag(sb, indent, "Unk57", Unk57.ToString());
            RelXml.ValueTag(sb, indent, "Unk58", Unk58.ToString());
            RelXml.ValueTag(sb, indent, "Unk59", FloatUtil.ToString(Unk59));
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            Unk01 = Xml.GetChildIntAttribute(node, "Unk01", "value");
            Unk02 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk02"));
            Unk03 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk03"));
            Unk04 = Xml.GetChildIntAttribute(node, "Unk04", "value");
            Unk05 = Xml.GetChildIntAttribute(node, "Unk05", "value");
            Unk06 = Xml.GetChildIntAttribute(node, "Unk06", "value");
            Unk07 = Xml.GetChildIntAttribute(node, "Unk07", "value");
            Unk08 = Xml.GetChildIntAttribute(node, "Unk08", "value");
            Unk09 = Xml.GetChildIntAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildIntAttribute(node, "Unk10", "value");
            Unk11 = Xml.GetChildIntAttribute(node, "Unk11", "value");
            Unk12 = Xml.GetChildIntAttribute(node, "Unk12", "value");
            Unk13 = Xml.GetChildIntAttribute(node, "Unk13", "value");
            Unk14 = Xml.GetChildIntAttribute(node, "Unk14", "value");
            Unk15 = Xml.GetChildIntAttribute(node, "Unk15", "value");
            Unk16 = Xml.GetChildIntAttribute(node, "Unk16", "value");
            Unk17 = Xml.GetChildIntAttribute(node, "Unk17", "value");
            Unk18 = Xml.GetChildIntAttribute(node, "Unk18", "value");
            Unk19 = Xml.GetChildIntAttribute(node, "Unk19", "value");
            Unk20 = Xml.GetChildIntAttribute(node, "Unk20", "value");
            Unk21 = Xml.GetChildIntAttribute(node, "Unk21", "value");
            Unk22 = Xml.GetChildFloatAttribute(node, "Unk22", "value");
            Unk23 = Xml.GetChildFloatAttribute(node, "Unk23", "value");
            Unk24 = Xml.GetChildFloatAttribute(node, "Unk24", "value");
            Unk25 = Xml.GetChildFloatAttribute(node, "Unk25", "value");
            Unk26 = Xml.GetChildFloatAttribute(node, "Unk26", "value");
            Unk27 = Xml.GetChildFloatAttribute(node, "Unk27", "value");
            Unk28 = Xml.GetChildFloatAttribute(node, "Unk28", "value");
            Unk29 = Xml.GetChildIntAttribute(node, "Unk29", "value");
            Unk30 = Xml.GetChildIntAttribute(node, "Unk30", "value");
            Unk31 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk31"));
            Unk32 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk32"));
            Unk33 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk33"));
            Unk34 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk34"));
            Unk35 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk35"));
            Unk36 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk36"));
            Unk37 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk37"));
            Unk38 = Xml.GetChildIntAttribute(node, "Unk38", "value");
            Unk39 = Xml.GetChildIntAttribute(node, "Unk39", "value");
            Unk40 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk40"));
            Unk41 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk41"));
            Unk42 = Xml.GetChildIntAttribute(node, "Unk42", "value");
            Unk43 = Xml.GetChildIntAttribute(node, "Unk43", "value");
            Unk44 = Xml.GetChildIntAttribute(node, "Unk44", "value");
            Unk45 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk45"));
            Unk46 = Xml.GetChildIntAttribute(node, "Unk46", "value");
            Unk47 = Xml.GetChildIntAttribute(node, "Unk47", "value");
            Unk48 = Xml.GetChildIntAttribute(node, "Unk48", "value");
            Unk49 = Xml.GetChildIntAttribute(node, "Unk49", "value");
            Unk50 = Xml.GetChildIntAttribute(node, "Unk50", "value");
            Unk51 = Xml.GetChildIntAttribute(node, "Unk51", "value");
            Unk52 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk52"));
            Unk53 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk53"));
            Unk54 = Xml.GetChildIntAttribute(node, "Unk54", "value");
            Unk55 = Xml.GetChildIntAttribute(node, "Unk55", "value");
            Unk56 = Xml.GetChildIntAttribute(node, "Unk56", "value");
            Unk57 = Xml.GetChildIntAttribute(node, "Unk57", "value");
            Unk58 = Xml.GetChildIntAttribute(node, "Unk58", "value");
            Unk59 = Xml.GetChildFloatAttribute(node, "Unk59", "value");
        }
    }
    [TC(typeof(EXP))] public class Dat151Vehicle : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; } //2432719400   0x91005A28
        public MetaHash Engine { get; set; }
        public MetaHash EngineGranular { get; set; }
        public MetaHash Unk03 { get; set; }
        public MetaHash DoorOpen { get; set; }
        public MetaHash DoorClose { get; set; }
        public MetaHash Unk06 { get; set; }
        public MetaHash Unk07 { get; set; }
        public MetaHash Unk08 { get; set; }
        public float Unk09 { get; set; }
        public MetaHash Unk10 { get; set; }
        public MetaHash Unk11 { get; set; }
        public float Unk12 { get; set; }
        public float Unk13 { get; set; }
        public MetaHash Unk14 { get; set; }//split? 0x00C2FB47..
        public int Unk15 { get; set; }
        public int Unk16 { get; set; }
        public int Unk17 { get; set; }
        public MetaHash ScannerParams { get; set; }
        public MetaHash JumpLandIntact { get; set; }
        public MetaHash JumpLandLoose { get; set; }
        public int Unk21 { get; set; }
        public int Unk22 { get; set; }
        public FlagsUint Unk23 { get; set; }
        //public FlagsUshort Unk23a { get; set; }//0x0002
        //public FlagsUshort Unk23b { get; set; }//0x0801
        public MetaHash Unk24 { get; set; }
        public MetaHash Unk25 { get; set; }
        public MetaHash Unk26 { get; set; }
        public FlagsUint Unk27 { get; set; }
        //public FlagsUshort Unk27a { get; set; }
        //public FlagsUshort Unk27b { get; set; }
        public MetaHash Unk28 { get; set; }
        public FlagsUint Unk29 { get; set; }
        //public FlagsUshort Unk29a { get; set; }//0x0070
        //public FlagsUshort Unk29b { get; set; }//0x55fc
        public MetaHash Unk30 { get; set; }
        public MetaHash Unk31 { get; set; }
        public MetaHash Unk32 { get; set; }
        public MetaHash Unk33 { get; set; }//flags??  0xB807DF3E
        public MetaHash Unk34 { get; set; }//flags??  0xE38FCF16
        public MetaHash Unk35 { get; set; }
        public MetaHash Unk36 { get; set; }
        public float Unk37 { get; set; }
        public float Unk38 { get; set; }
        public MetaHash Unk39 { get; set; }
        public int Unk40 { get; set; }
        public MetaHash Unk41 { get; set; }//flags? 0x49DF3CF8   0x8E53EC78
        public int Unk42 { get; set; }
        public int Unk43 { get; set; }
        public int Unk44 { get; set; }
        public MetaHash Unk45 { get; set; }
        public MetaHash Unk46 { get; set; }
        public MetaHash Unk47 { get; set; }//flags?  0x83FC62DA
        public MetaHash TurretSounds { get; set; }
        public int Unk49 { get; set; }
        public MetaHash Unk50 { get; set; }//flags? 0x65A95A8B, 0x85439DAD
        public MetaHash Unk51 { get; set; }//flags? 0x6213618E, 0x990D0483
        public int Unk52 { get; set; }
        public MetaHash Unk53 { get; set; }//flags?  0x04D73241, 0x7F471776
        public float Unk54 { get; set; }
        public MetaHash Unk55 { get; set; }
        public int Unk56 { get; set; }
        public MetaHash Unk57 { get; set; }
        public MetaHash Unk58 { get; set; }
        public float Unk59 { get; set; }
        public int Unk60 { get; set; }
        public float Unk61 { get; set; }
        public int Unk62 { get; set; }
        public MetaHash Unk63 { get; set; }
        public int Unk64 { get; set; }
        public ushort Unk65 { get; set; }
        public ushort Unk66 { get; set; }
        public MetaHash Unk67 { get; set; }
        public MetaHash Unk68 { get; set; }
        public MetaHash Unk69 { get; set; }
        public int Unk70 { get; set; }
        public MetaHash Unk71 { get; set; }
        public MetaHash Unk72 { get; set; }

        public Dat151Vehicle(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Vehicle;
            TypeID = (byte)Type;
        }
        public Dat151Vehicle(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32(); //2432719400   0x91005A28
            Engine = br.ReadUInt32();
            EngineGranular = br.ReadUInt32();
            Unk03 = br.ReadUInt32();
            DoorOpen = br.ReadUInt32();
            DoorClose = br.ReadUInt32();
            Unk06 = br.ReadUInt32();
            Unk07 = br.ReadUInt32();
            Unk08 = br.ReadUInt32();
            Unk09 = br.ReadSingle();
            Unk10 = br.ReadUInt32();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadSingle();
            Unk13 = br.ReadSingle();
            Unk14 = br.ReadUInt32();//split? 0x00C2FB47..
            Unk15 = br.ReadInt32();
            Unk16 = br.ReadInt32();
            Unk17 = br.ReadInt32();
            ScannerParams = br.ReadUInt32();
            JumpLandIntact = br.ReadUInt32();
            JumpLandLoose = br.ReadUInt32();
            Unk21 = br.ReadInt32();
            Unk22 = br.ReadInt32();
            Unk23 = br.ReadUInt32();
            //Unk23a = br.ReadUInt16();//0x0002
            //Unk23b = br.ReadUInt16();//0x0801
            Unk24 = br.ReadUInt32();
            Unk25 = br.ReadUInt32();
            Unk26 = br.ReadUInt32();
            Unk27 = br.ReadUInt32();
            //Unk27a = br.ReadUInt16();
            //Unk27b = br.ReadUInt16();
            Unk28 = br.ReadUInt32();
            Unk29 = br.ReadUInt32();
            //Unk29a = br.ReadUInt16();//0x0070
            //Unk29b = br.ReadUInt16();//0x55fc
            Unk30 = br.ReadUInt32();
            Unk31 = br.ReadUInt32();
            Unk32 = br.ReadUInt32();
            Unk33 = br.ReadUInt32();//flags??  0xB807DF3E
            Unk34 = br.ReadUInt32();//flags??  0xE38FCF16
            Unk35 = br.ReadUInt32();
            Unk36 = br.ReadUInt32();
            Unk37 = br.ReadSingle();
            Unk38 = br.ReadSingle();
            Unk39 = br.ReadUInt32();
            Unk40 = br.ReadInt32();
            Unk41 = br.ReadUInt32();//flags? 0x49DF3CF8   0x8E53EC78
            Unk42 = br.ReadInt32();
            Unk43 = br.ReadInt32();
            Unk44 = br.ReadInt32();
            Unk45 = br.ReadUInt32();
            Unk46 = br.ReadUInt32();
            Unk47 = br.ReadUInt32();//flags?  0x83FC62DA
            TurretSounds = br.ReadUInt32();
            Unk49 = br.ReadInt32();
            Unk50 = br.ReadUInt32();//flags? 0x65A95A8B, 0x85439DAD
            Unk51 = br.ReadUInt32();//flags? 0x6213618E, 0x990D0483
            Unk52 = br.ReadInt32();
            Unk53 = br.ReadUInt32();//flags?  0x04D73241, 0x7F471776
            Unk54 = br.ReadSingle();
            Unk55 = br.ReadUInt32();
            Unk56 = br.ReadInt32();
            Unk57 = br.ReadUInt32();
            Unk58 = br.ReadUInt32();
            Unk59 = br.ReadSingle();
            Unk60 = br.ReadInt32();
            Unk61 = br.ReadSingle();
            Unk62 = br.ReadInt32();



            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            switch (bytesleft) //any other way to tell..?
            {
                case 0:
                    break;
                case 4:
                    Unk63 = br.ReadUInt32();//flags??  0xE38FCF16
                    if (Unk63 == 0)
                    { }
                    break;
                case 36:
                    Unk63 = br.ReadUInt32();//flags??  0xE38FCF16
                    Unk64 = br.ReadInt32();
                    Unk65 = br.ReadUInt16();
                    Unk66 = br.ReadUInt16();
                    Unk67 = br.ReadUInt32();//flags?  0x536F6CAC
                    Unk68 = br.ReadUInt32();//flags??  0xE38FCF16
                    Unk69 = br.ReadUInt32();//flags?  0x7C9B8D8C
                    Unk70 = br.ReadInt32();
                    Unk71 = br.ReadUInt32();//flags??  0xE38FCF16
                    Unk72 = br.ReadUInt32();//flags??  0xE38FCF16
                    if (Unk70 != 0)
                    { }
                    if (Unk68 == 0)
                    { }
                    if (Unk71 == 0)
                    { }
                    if (Unk72 == 0)
                    { }

                    break;
                default:
                    break;
            }
            if (bytesleft != 0)
            { }




            if (Unk15 != 0)
            { }
            if (Unk16 != 0)
            { }
            if (Unk17 != 0)
            { }
            if (Unk40 != 0)
            { }
            if (Unk42 != 0)
            { }
            if (Unk43 != 0)
            { }
            if (Unk44 != 0)
            { }

            switch (Unk21)
            {
                case 31:
                case 0:
                    break;
                default:
                    break;
            }
            switch (Unk22)
            {
                case 36:
                case 100:
                case 1:
                    break;
                default:
                    break;
            }
            switch (Unk49)
            {
                case 8:
                case 5:
                case 3:
                case 1:
                case 4:
                case 0:
                case 6:
                case 7:
                    break;
                default:
                    break;
            }
            switch (Unk56)
            {
                case 2:
                case 3:
                case 0:
                case 1:
                    break;
                default:
                    break;
            }

        }
        public override void Write(BinaryWriter bw)
        {
            //base.Write(bw);
            //return;

            WriteTypeAndOffset(bw);

            bw.Write(Unk00); //2432719400   0x91005A28
            bw.Write(Engine);
            bw.Write(EngineGranular);
            bw.Write(Unk03);
            bw.Write(DoorOpen);
            bw.Write(DoorClose);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);
            bw.Write(Unk14);//split? 0x00C2FB47..
            bw.Write(Unk15);
            bw.Write(Unk16);
            bw.Write(Unk17);
            bw.Write(ScannerParams);
            bw.Write(JumpLandIntact);
            bw.Write(JumpLandLoose);
            bw.Write(Unk21);
            bw.Write(Unk22);
            bw.Write(Unk23);
            //bw.Write(Unk23a);//0x0002
            //bw.Write(Unk23b);//0x0801
            bw.Write(Unk24);
            bw.Write(Unk25);
            bw.Write(Unk26);
            bw.Write(Unk27);
            //bw.Write(Unk27a);
            //bw.Write(Unk27b);
            bw.Write(Unk28);
            bw.Write(Unk29);
            //bw.Write(Unk29a);//0x0070
            //bw.Write(Unk29b);//0x55fc
            bw.Write(Unk30);
            bw.Write(Unk31);
            bw.Write(Unk32);
            bw.Write(Unk33);//flags??  0xB807DF3E
            bw.Write(Unk34);//flags??  0xE38FCF16
            bw.Write(Unk35);
            bw.Write(Unk36);
            bw.Write(Unk37);
            bw.Write(Unk38);
            bw.Write(Unk39);
            bw.Write(Unk40);
            bw.Write(Unk41);//flags? 0x49DF3CF8   0x8E53EC78
            bw.Write(Unk42);
            bw.Write(Unk43);
            bw.Write(Unk44);
            bw.Write(Unk45);
            bw.Write(Unk46);
            bw.Write(Unk47);//flags?  0x83FC62DA
            bw.Write(TurretSounds);
            bw.Write(Unk49);
            bw.Write(Unk50);//flags? 0x65A95A8B, 0x85439DAD
            bw.Write(Unk51);//flags? 0x6213618E, 0x990D0483
            bw.Write(Unk52);
            bw.Write(Unk53);//flags?  0x04D73241, 0x7F471776
            bw.Write(Unk54);
            bw.Write(Unk55);
            bw.Write(Unk56);
            bw.Write(Unk57);
            bw.Write(Unk58);
            bw.Write(Unk59);
            bw.Write(Unk60);
            bw.Write(Unk61);
            bw.Write(Unk62);

            if (Unk63 != 0)//any better way?
            {
                bw.Write(Unk63);

                if ((Unk68 != 0)||(Unk71 != 0) ||(Unk72 != 0))//any better way?
                {
                    bw.Write(Unk64);
                    bw.Write(Unk65);
                    bw.Write(Unk66);
                    bw.Write(Unk67);//flags?  0x536F6CAC
                    bw.Write(Unk68);//flags??  0xE38FCF16
                    bw.Write(Unk69);//flags?  0x7C9B8D8C
                    bw.Write(Unk70);
                    bw.Write(Unk71);//flags??  0xE38FCF16
                    bw.Write(Unk72);//flags??  0xE38FCF16
                }
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            RelXml.StringTag(sb, indent, "Engine", RelXml.HashString(Engine));
            RelXml.StringTag(sb, indent, "EngineGranular", RelXml.HashString(EngineGranular));
            RelXml.StringTag(sb, indent, "Unk03", RelXml.HashString(Unk03));
            RelXml.StringTag(sb, indent, "DoorOpen", RelXml.HashString(DoorOpen));
            RelXml.StringTag(sb, indent, "DoorClose", RelXml.HashString(DoorClose));
            RelXml.StringTag(sb, indent, "Unk06", RelXml.HashString(Unk06));
            RelXml.StringTag(sb, indent, "Unk07", RelXml.HashString(Unk07));
            RelXml.StringTag(sb, indent, "Unk08", RelXml.HashString(Unk08));
            RelXml.ValueTag(sb, indent, "Unk09", FloatUtil.ToString(Unk09));
            RelXml.StringTag(sb, indent, "Unk10", RelXml.HashString(Unk10));
            RelXml.StringTag(sb, indent, "Unk11", RelXml.HashString(Unk11));
            RelXml.ValueTag(sb, indent, "Unk12", FloatUtil.ToString(Unk12));
            RelXml.ValueTag(sb, indent, "Unk13", FloatUtil.ToString(Unk13));
            RelXml.StringTag(sb, indent, "Unk14", RelXml.HashString(Unk14));
            RelXml.ValueTag(sb, indent, "Unk15", Unk15.ToString());
            RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
            RelXml.ValueTag(sb, indent, "Unk17", Unk17.ToString());
            RelXml.StringTag(sb, indent, "ScannerParams", RelXml.HashString(ScannerParams));
            RelXml.StringTag(sb, indent, "JumpLandIntact", RelXml.HashString(JumpLandIntact));
            RelXml.StringTag(sb, indent, "JumpLandLoose", RelXml.HashString(JumpLandLoose));
            RelXml.ValueTag(sb, indent, "Unk21", Unk21.ToString());
            RelXml.ValueTag(sb, indent, "Unk22", Unk22.ToString());
            RelXml.ValueTag(sb, indent, "Unk23", "0x" + Unk23.Hex);
            RelXml.StringTag(sb, indent, "Unk24", RelXml.HashString(Unk24));
            RelXml.StringTag(sb, indent, "Unk25", RelXml.HashString(Unk25));
            RelXml.StringTag(sb, indent, "Unk26", RelXml.HashString(Unk26));
            RelXml.ValueTag(sb, indent, "Unk27", "0x" + Unk27.Hex);
            RelXml.StringTag(sb, indent, "Unk28", RelXml.HashString(Unk28));
            RelXml.ValueTag(sb, indent, "Unk29", "0x" + Unk29.Hex);
            RelXml.StringTag(sb, indent, "Unk30", RelXml.HashString(Unk30));
            RelXml.StringTag(sb, indent, "Unk31", RelXml.HashString(Unk31));
            RelXml.StringTag(sb, indent, "Unk32", RelXml.HashString(Unk32));
            RelXml.StringTag(sb, indent, "Unk33", RelXml.HashString(Unk33));
            RelXml.StringTag(sb, indent, "Unk34", RelXml.HashString(Unk34));
            RelXml.StringTag(sb, indent, "Unk35", RelXml.HashString(Unk35));
            RelXml.StringTag(sb, indent, "Unk36", RelXml.HashString(Unk36));
            RelXml.ValueTag(sb, indent, "Unk37", FloatUtil.ToString(Unk37));
            RelXml.ValueTag(sb, indent, "Unk38", FloatUtil.ToString(Unk38));
            RelXml.StringTag(sb, indent, "Unk39", RelXml.HashString(Unk39));
            RelXml.ValueTag(sb, indent, "Unk40", Unk40.ToString());
            RelXml.StringTag(sb, indent, "Unk41", RelXml.HashString(Unk41));
            RelXml.ValueTag(sb, indent, "Unk42", Unk42.ToString());
            RelXml.ValueTag(sb, indent, "Unk43", Unk43.ToString());
            RelXml.ValueTag(sb, indent, "Unk44", Unk44.ToString());
            RelXml.StringTag(sb, indent, "Unk45", RelXml.HashString(Unk45));
            RelXml.StringTag(sb, indent, "Unk46", RelXml.HashString(Unk46));
            RelXml.StringTag(sb, indent, "Unk47", RelXml.HashString(Unk47));
            RelXml.StringTag(sb, indent, "TurretSounds", RelXml.HashString(TurretSounds));
            RelXml.ValueTag(sb, indent, "Unk49", Unk49.ToString());
            RelXml.StringTag(sb, indent, "Unk50", RelXml.HashString(Unk50));
            RelXml.StringTag(sb, indent, "Unk51", RelXml.HashString(Unk51));
            RelXml.ValueTag(sb, indent, "Unk52", Unk52.ToString());
            RelXml.StringTag(sb, indent, "Unk53", RelXml.HashString(Unk53));
            RelXml.ValueTag(sb, indent, "Unk54", FloatUtil.ToString(Unk54));
            RelXml.StringTag(sb, indent, "Unk55", RelXml.HashString(Unk55));
            RelXml.ValueTag(sb, indent, "Unk56", Unk56.ToString());
            RelXml.StringTag(sb, indent, "Unk57", RelXml.HashString(Unk57));
            RelXml.StringTag(sb, indent, "Unk58", RelXml.HashString(Unk58));
            RelXml.ValueTag(sb, indent, "Unk59", FloatUtil.ToString(Unk59));
            RelXml.ValueTag(sb, indent, "Unk60", Unk60.ToString());
            RelXml.ValueTag(sb, indent, "Unk61", FloatUtil.ToString(Unk61));
            RelXml.ValueTag(sb, indent, "Unk62", Unk62.ToString());
            RelXml.StringTag(sb, indent, "Unk63", RelXml.HashString(Unk63));
            RelXml.ValueTag(sb, indent, "Unk64", Unk64.ToString());
            RelXml.ValueTag(sb, indent, "Unk65", Unk65.ToString());
            RelXml.ValueTag(sb, indent, "Unk66", Unk66.ToString());
            RelXml.StringTag(sb, indent, "Unk67", RelXml.HashString(Unk67));
            RelXml.StringTag(sb, indent, "Unk68", RelXml.HashString(Unk68));
            RelXml.StringTag(sb, indent, "Unk69", RelXml.HashString(Unk69));
            RelXml.ValueTag(sb, indent, "Unk70", Unk70.ToString());
            RelXml.StringTag(sb, indent, "Unk71", RelXml.HashString(Unk71));
            RelXml.StringTag(sb, indent, "Unk72", RelXml.HashString(Unk72));
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            Engine = XmlRel.GetHash(Xml.GetChildInnerText(node, "Engine"));
            EngineGranular = XmlRel.GetHash(Xml.GetChildInnerText(node, "EngineGranular"));
            Unk03 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk03"));
            DoorOpen = XmlRel.GetHash(Xml.GetChildInnerText(node, "DoorOpen"));
            DoorClose = XmlRel.GetHash(Xml.GetChildInnerText(node, "DoorClose"));
            Unk06 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk06"));
            Unk07 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk07"));
            Unk08 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk08"));
            Unk09 = Xml.GetChildFloatAttribute(node, "Unk09", "value");
            Unk10 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk10"));
            Unk11 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk11"));
            Unk12 = Xml.GetChildFloatAttribute(node, "Unk12", "value");
            Unk13 = Xml.GetChildFloatAttribute(node, "Unk13", "value");
            Unk14 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk14"));
            Unk15 = Xml.GetChildIntAttribute(node, "Unk15", "value");
            Unk16 = Xml.GetChildIntAttribute(node, "Unk16", "value");
            Unk17 = Xml.GetChildIntAttribute(node, "Unk17", "value");
            ScannerParams = XmlRel.GetHash(Xml.GetChildInnerText(node, "ScannerParams"));
            JumpLandIntact = XmlRel.GetHash(Xml.GetChildInnerText(node, "JumpLandIntact"));
            JumpLandLoose = XmlRel.GetHash(Xml.GetChildInnerText(node, "JumpLandLoose"));
            Unk21 = Xml.GetChildIntAttribute(node, "Unk21", "value");
            Unk22 = Xml.GetChildIntAttribute(node, "Unk22", "value");
            Unk23 = Xml.GetChildUIntAttribute(node, "Unk23", "value");
            Unk24 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk24"));
            Unk25 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk25"));
            Unk26 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk26"));
            Unk27 = Xml.GetChildUIntAttribute(node, "Unk27", "value");
            Unk28 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk28"));
            Unk29 = Xml.GetChildUIntAttribute(node, "Unk29", "value");
            Unk30 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk30"));
            Unk31 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk31"));
            Unk32 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk32"));
            Unk33 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk33"));
            Unk34 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk34"));
            Unk35 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk35"));
            Unk36 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk36"));
            Unk37 = Xml.GetChildFloatAttribute(node, "Unk37", "value");
            Unk38 = Xml.GetChildFloatAttribute(node, "Unk38", "value");
            Unk39 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk39"));
            Unk40 = Xml.GetChildIntAttribute(node, "Unk40", "value");
            Unk41 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk41"));
            Unk42 = Xml.GetChildIntAttribute(node, "Unk42", "value");
            Unk43 = Xml.GetChildIntAttribute(node, "Unk43", "value");
            Unk44 = Xml.GetChildIntAttribute(node, "Unk44", "value");
            Unk45 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk45"));
            Unk46 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk46"));
            Unk47 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk47"));
            TurretSounds = XmlRel.GetHash(Xml.GetChildInnerText(node, "TurretSounds"));
            Unk49 = Xml.GetChildIntAttribute(node, "Unk49", "value");
            Unk50 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk50"));
            Unk51 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk51"));
            Unk52 = Xml.GetChildIntAttribute(node, "Unk52", "value");
            Unk53 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk53"));
            Unk54 = Xml.GetChildFloatAttribute(node, "Unk54", "value");
            Unk55 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk55"));
            Unk56 = Xml.GetChildIntAttribute(node, "Unk56", "value");
            Unk57 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk57"));
            Unk58 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk58"));
            Unk59 = Xml.GetChildFloatAttribute(node, "Unk59", "value");
            Unk60 = Xml.GetChildIntAttribute(node, "Unk60", "value");
            Unk61 = Xml.GetChildFloatAttribute(node, "Unk61", "value");
            Unk62 = Xml.GetChildIntAttribute(node, "Unk62", "value");
            Unk63 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk63"));
            Unk64 = Xml.GetChildIntAttribute(node, "Unk64", "value");
            Unk65 = (ushort)Xml.GetChildUIntAttribute(node, "Unk65", "value");
            Unk66 = (ushort)Xml.GetChildUIntAttribute(node, "Unk66", "value");
            Unk67 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk67"));
            Unk68 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk68"));
            Unk69 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk69"));
            Unk70 = Xml.GetChildIntAttribute(node, "Unk70", "value");
            Unk71 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk71"));
            Unk72 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk72"));
        }
    }
    [TC(typeof(EXP))] public class Dat151VehicleEngine : Dat151RelData
    {
        public int Unk00 { get; set; }
        public int Unk01 { get; set; }
        public int Unk02 { get; set; }
        public int Unk03 { get; set; }
        public MetaHash Unk04 { get; set; }
        public MetaHash Unk05 { get; set; }
        public MetaHash Unk06 { get; set; }
        public MetaHash Unk07 { get; set; }
        public MetaHash Unk08 { get; set; }
        public int Unk09 { get; set; }
        public int Unk10 { get; set; }
        public MetaHash Unk11 { get; set; }
        public MetaHash Unk12 { get; set; }//flags? float?
        public int Unk13 { get; set; }
        public int Unk14 { get; set; }
        public MetaHash Unk15 { get; set; }
        public int Unk16 { get; set; }
        public int Unk17 { get; set; }
        public MetaHash Turbo { get; set; }
        public int Unk19 { get; set; }
        public int Unk20 { get; set; }
        public MetaHash Unk21 { get; set; }
        public int Unk22 { get; set; }
        public int Unk23 { get; set; }
        public MetaHash Transmission { get; set; }
        public int Unk25 { get; set; }
        public int Unk26 { get; set; }
        public int Unk27 { get; set; }
        public MetaHash Ignition { get; set; }
        public MetaHash ShutDown { get; set; }
        public MetaHash Unk30 { get; set; }
        public MetaHash ExhaustPops { get; set; }
        public MetaHash Unk32 { get; set; }
        public int Unk33 { get; set; }
        public int Unk34 { get; set; }
        public MetaHash StartupMaster { get; set; }
        public MetaHash Unk36 { get; set; }
        public MetaHash Unk37 { get; set; }
        public MetaHash Unk38 { get; set; }
        public MetaHash Unk39 { get; set; }//flags? separate?
        public MetaHash Unk40 { get; set; }
        public MetaHash Unk41 { get; set; }
        public int Unk42 { get; set; }
        public MetaHash Unk43 { get; set; }
        public MetaHash Unk44 { get; set; }//flags? separate?
        public int Unk45 { get; set; }
        public int Unk46 { get; set; }
        public int Unk47 { get; set; }
        public int Unk48 { get; set; }
        public int Unk49 { get; set; }
        public int Unk50 { get; set; }
        public MetaHash DumpValve { get; set; }
        public int Unk52 { get; set; }
        public MetaHash TransmissionUpgraded { get; set; }
        public MetaHash TurboUpgraded { get; set; }
        public MetaHash Unk55 { get; set; }
        public MetaHash ExhaustPopsUpgraded { get; set; }

        public Dat151VehicleEngine(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.VehicleEngine;
            TypeID = (byte)Type;
        }
        public Dat151VehicleEngine(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadInt32();
            Unk01 = br.ReadInt32();
            Unk02 = br.ReadInt32();
            Unk03 = br.ReadInt32();
            Unk04 = br.ReadUInt32();
            Unk05 = br.ReadUInt32();
            Unk06 = br.ReadUInt32();
            Unk07 = br.ReadUInt32();
            Unk08 = br.ReadUInt32();
            Unk09 = br.ReadInt32();
            Unk10 = br.ReadInt32();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadUInt32();//flags? float?
            Unk13 = br.ReadInt32();
            Unk14 = br.ReadInt32();
            Unk15 = br.ReadUInt32();
            Unk16 = br.ReadInt32();
            Unk17 = br.ReadInt32();
            Turbo = br.ReadUInt32();
            Unk19 = br.ReadInt32();
            Unk20 = br.ReadInt32();
            Unk21 = br.ReadUInt32();
            Unk22 = br.ReadInt32();
            Unk23 = br.ReadInt32();
            Transmission = br.ReadUInt32();
            Unk25 = br.ReadInt32();
            Unk26 = br.ReadInt32();
            Unk27 = br.ReadInt32();
            Ignition = br.ReadUInt32();
            ShutDown = br.ReadUInt32();
            Unk30 = br.ReadUInt32();
            ExhaustPops = br.ReadUInt32();
            Unk32 = br.ReadUInt32();
            Unk33 = br.ReadInt32();
            Unk34 = br.ReadInt32();
            StartupMaster = br.ReadUInt32();
            Unk36 = br.ReadUInt32();
            Unk37 = br.ReadUInt32();
            Unk38 = br.ReadUInt32();
            Unk39 = br.ReadUInt32();//flags? separate?
            Unk40 = br.ReadUInt32();
            Unk41 = br.ReadUInt32();
            Unk42 = br.ReadInt32();
            Unk43 = br.ReadUInt32();
            Unk44 = br.ReadUInt32();//flags? separate?
            Unk45 = br.ReadInt32();
            Unk46 = br.ReadInt32();
            Unk47 = br.ReadInt32();
            Unk48 = br.ReadInt32();
            Unk49 = br.ReadInt32();
            Unk50 = br.ReadInt32();
            DumpValve = br.ReadUInt32();//float?
            Unk52 = br.ReadInt32();
            TransmissionUpgraded = br.ReadUInt32();
            TurboUpgraded = br.ReadUInt32();
            Unk55 = br.ReadUInt32();
            ExhaustPopsUpgraded = br.ReadUInt32();

            if ((Unk38 != 0) && (Unk38 != 0x4022A088))//float?
            { }
            if (Unk46 != 0)
            { }
            if (Unk47 != 0)
            { }
            if (Unk49 != 0)
            { }
            if (Unk50 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }

        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk00);
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);//flags? float?
            bw.Write(Unk13);
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(Unk16);
            bw.Write(Unk17);
            bw.Write(Turbo);
            bw.Write(Unk19);
            bw.Write(Unk20);
            bw.Write(Unk21);
            bw.Write(Unk22);
            bw.Write(Unk23);
            bw.Write(Transmission);
            bw.Write(Unk25);
            bw.Write(Unk26);
            bw.Write(Unk27);
            bw.Write(Ignition);
            bw.Write(ShutDown);
            bw.Write(Unk30);
            bw.Write(ExhaustPops);
            bw.Write(Unk32);
            bw.Write(Unk33);
            bw.Write(Unk34);
            bw.Write(StartupMaster);
            bw.Write(Unk36);
            bw.Write(Unk37);
            bw.Write(Unk38);
            bw.Write(Unk39);//flags? separate?
            bw.Write(Unk40);
            bw.Write(Unk41);
            bw.Write(Unk42);
            bw.Write(Unk43);
            bw.Write(Unk44);//flags? separate?
            bw.Write(Unk45);
            bw.Write(Unk46);
            bw.Write(Unk47);
            bw.Write(Unk48);
            bw.Write(Unk49);
            bw.Write(Unk50);
            bw.Write(DumpValve);//float?
            bw.Write(Unk52);
            bw.Write(TransmissionUpgraded);
            bw.Write(TurboUpgraded);
            bw.Write(Unk55);
            bw.Write(ExhaustPopsUpgraded);

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", Unk00.ToString());
            RelXml.ValueTag(sb, indent, "Unk01", Unk01.ToString());
            RelXml.ValueTag(sb, indent, "Unk02", Unk02.ToString());
            RelXml.ValueTag(sb, indent, "Unk03", Unk03.ToString());
            RelXml.StringTag(sb, indent, "Unk04", RelXml.HashString(Unk04));
            RelXml.StringTag(sb, indent, "Unk05", RelXml.HashString(Unk05));
            RelXml.StringTag(sb, indent, "Unk06", RelXml.HashString(Unk06));
            RelXml.StringTag(sb, indent, "Unk07", RelXml.HashString(Unk07));
            RelXml.StringTag(sb, indent, "Unk08", RelXml.HashString(Unk08));
            RelXml.ValueTag(sb, indent, "Unk09", Unk09.ToString());
            RelXml.ValueTag(sb, indent, "Unk10", Unk10.ToString());
            RelXml.StringTag(sb, indent, "Unk11", RelXml.HashString(Unk11));
            RelXml.StringTag(sb, indent, "Unk12", RelXml.HashString(Unk12));
            RelXml.ValueTag(sb, indent, "Unk13", Unk13.ToString());
            RelXml.ValueTag(sb, indent, "Unk14", Unk14.ToString());
            RelXml.StringTag(sb, indent, "Unk15", RelXml.HashString(Unk15));
            RelXml.ValueTag(sb, indent, "Unk16", Unk16.ToString());
            RelXml.ValueTag(sb, indent, "Unk17", Unk17.ToString());
            RelXml.StringTag(sb, indent, "Turbo", RelXml.HashString(Turbo));
            RelXml.ValueTag(sb, indent, "Unk19", Unk19.ToString());
            RelXml.ValueTag(sb, indent, "Unk20", Unk20.ToString());
            RelXml.StringTag(sb, indent, "Unk21", RelXml.HashString(Unk21));
            RelXml.ValueTag(sb, indent, "Unk22", Unk22.ToString());
            RelXml.ValueTag(sb, indent, "Unk23", Unk23.ToString());
            RelXml.StringTag(sb, indent, "Transmission", RelXml.HashString(Transmission));
            RelXml.ValueTag(sb, indent, "Unk25", Unk25.ToString());
            RelXml.ValueTag(sb, indent, "Unk26", Unk26.ToString());
            RelXml.ValueTag(sb, indent, "Unk27", Unk27.ToString());
            RelXml.StringTag(sb, indent, "Ignition", RelXml.HashString(Ignition));
            RelXml.StringTag(sb, indent, "ShutDown", RelXml.HashString(ShutDown));
            RelXml.StringTag(sb, indent, "Unk30", RelXml.HashString(Unk30));
            RelXml.StringTag(sb, indent, "ExhaustPops", RelXml.HashString(ExhaustPops));
            RelXml.StringTag(sb, indent, "Unk32", RelXml.HashString(Unk32));
            RelXml.ValueTag(sb, indent, "Unk33", Unk33.ToString());
            RelXml.ValueTag(sb, indent, "Unk34", Unk34.ToString());
            RelXml.StringTag(sb, indent, "StartupMaster", RelXml.HashString(StartupMaster));
            RelXml.StringTag(sb, indent, "Unk36", RelXml.HashString(Unk36));
            RelXml.StringTag(sb, indent, "Unk37", RelXml.HashString(Unk37));
            RelXml.StringTag(sb, indent, "Unk38", RelXml.HashString(Unk38));
            RelXml.StringTag(sb, indent, "Unk39", RelXml.HashString(Unk39));
            RelXml.StringTag(sb, indent, "Unk40", RelXml.HashString(Unk40));
            RelXml.StringTag(sb, indent, "Unk41", RelXml.HashString(Unk41));
            RelXml.ValueTag(sb, indent, "Unk42", Unk42.ToString());
            RelXml.StringTag(sb, indent, "Unk43", RelXml.HashString(Unk43));
            RelXml.StringTag(sb, indent, "Unk44", RelXml.HashString(Unk44));
            RelXml.ValueTag(sb, indent, "Unk45", Unk45.ToString());
            RelXml.ValueTag(sb, indent, "Unk46", Unk46.ToString());
            RelXml.ValueTag(sb, indent, "Unk47", Unk47.ToString());
            RelXml.ValueTag(sb, indent, "Unk48", Unk48.ToString());
            RelXml.ValueTag(sb, indent, "Unk49", Unk49.ToString());
            RelXml.ValueTag(sb, indent, "Unk50", Unk50.ToString());
            RelXml.StringTag(sb, indent, "DumpValve", RelXml.HashString(DumpValve));
            RelXml.ValueTag(sb, indent, "Unk52", Unk52.ToString());
            RelXml.StringTag(sb, indent, "TransmissionUpgraded", RelXml.HashString(TransmissionUpgraded));
            RelXml.StringTag(sb, indent, "TurboUpgraded", RelXml.HashString(TurboUpgraded));
            RelXml.StringTag(sb, indent, "Unk55", RelXml.HashString(Unk55));
            RelXml.StringTag(sb, indent, "ExhaustPopsUpgraded", RelXml.HashString(ExhaustPopsUpgraded));
        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildIntAttribute(node, "Unk00", "value");
            Unk01 = Xml.GetChildIntAttribute(node, "Unk01", "value");
            Unk02 = Xml.GetChildIntAttribute(node, "Unk02", "value");
            Unk03 = Xml.GetChildIntAttribute(node, "Unk03", "value");
            Unk04 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk04"));
            Unk05 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk05"));
            Unk06 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk06"));
            Unk07 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk07"));
            Unk08 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk08"));
            Unk09 = Xml.GetChildIntAttribute(node, "Unk09", "value");
            Unk10 = Xml.GetChildIntAttribute(node, "Unk10", "value");
            Unk11 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk11"));
            Unk12 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk12"));
            Unk13 = Xml.GetChildIntAttribute(node, "Unk13", "value");
            Unk14 = Xml.GetChildIntAttribute(node, "Unk14", "value");
            Unk15 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk15"));
            Unk16 = Xml.GetChildIntAttribute(node, "Unk16", "value");
            Unk17 = Xml.GetChildIntAttribute(node, "Unk17", "value");
            Turbo = XmlRel.GetHash(Xml.GetChildInnerText(node, "Turbo"));
            Unk19 = Xml.GetChildIntAttribute(node, "Unk19", "value");
            Unk20 = Xml.GetChildIntAttribute(node, "Unk20", "value");
            Unk21 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk21"));
            Unk22 = Xml.GetChildIntAttribute(node, "Unk22", "value");
            Unk23 = Xml.GetChildIntAttribute(node, "Unk23", "value");
            Transmission = XmlRel.GetHash(Xml.GetChildInnerText(node, "Transmission"));
            Unk25 = Xml.GetChildIntAttribute(node, "Unk25", "value");
            Unk26 = Xml.GetChildIntAttribute(node, "Unk26", "value");
            Unk27 = Xml.GetChildIntAttribute(node, "Unk27", "value");
            Ignition = XmlRel.GetHash(Xml.GetChildInnerText(node, "Ignition"));
            ShutDown = XmlRel.GetHash(Xml.GetChildInnerText(node, "ShutDown"));
            Unk30 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk30"));
            ExhaustPops = XmlRel.GetHash(Xml.GetChildInnerText(node, "ExhaustPops"));
            Unk32 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk32"));
            Unk33 = Xml.GetChildIntAttribute(node, "Unk33", "value");
            Unk34 = Xml.GetChildIntAttribute(node, "Unk34", "value");
            StartupMaster = XmlRel.GetHash(Xml.GetChildInnerText(node, "StartupMaster"));
            Unk36 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk36"));
            Unk37 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk37"));
            Unk38 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk38"));
            Unk39 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk39"));
            Unk40 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk40"));
            Unk41 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk41"));
            Unk42 = Xml.GetChildIntAttribute(node, "Unk42", "value");
            Unk43 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk43"));
            Unk44 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk44"));
            Unk45 = Xml.GetChildIntAttribute(node, "Unk45", "value");
            Unk46 = Xml.GetChildIntAttribute(node, "Unk46", "value");
            Unk47 = Xml.GetChildIntAttribute(node, "Unk47", "value");
            Unk48 = Xml.GetChildIntAttribute(node, "Unk48", "value");
            Unk49 = Xml.GetChildIntAttribute(node, "Unk49", "value");
            Unk50 = Xml.GetChildIntAttribute(node, "Unk50", "value");
            DumpValve = XmlRel.GetHash(Xml.GetChildInnerText(node, "DumpValve"));
            Unk52 = Xml.GetChildIntAttribute(node, "Unk52", "value");
            TransmissionUpgraded = XmlRel.GetHash(Xml.GetChildInnerText(node, "TransmissionUpgraded"));
            TurboUpgraded = XmlRel.GetHash(Xml.GetChildInnerText(node, "TurboUpgraded"));
            Unk55 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk55"));
            ExhaustPopsUpgraded = XmlRel.GetHash(Xml.GetChildInnerText(node, "ExhaustPopsUpgraded"));
        }
    }
    [TC(typeof(EXP))] public class Dat151VehicleScannerParams : Dat151RelData
    {
        public FlagsUint Flags { get; set; }
        public int ParamCount { get; set; }
        public Param[] Params;

        public class Param
        {
            public MetaHash Unk01 { get; set; }
            public MetaHash Unk02 { get; set; }
            public MetaHash Unk03 { get; set; }
            public MetaHash Unk04 { get; set; }
        }

        public Dat151VehicleScannerParams(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.VehicleScannerParams;
            TypeID = (byte)Type;
        }
        public Dat151VehicleScannerParams(RelData d, BinaryReader br) : base(d, br)
        {
            Flags = br.ReadUInt32();
            ParamCount = br.ReadInt32();
            Params = new Param[ParamCount];
            for (int i = 0; i < ParamCount; i++)
            {
                var p = new Param();
                p.Unk01 = br.ReadUInt32();
                p.Unk02 = br.ReadUInt32();
                p.Unk03 = br.ReadUInt32();
                p.Unk04 = br.ReadUInt32();
                Params[i] = p;
            }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Flags);
            bw.Write(ParamCount);
            for (int i = 0; i < ParamCount; i++)
            {
                var p = Params[i];
                bw.Write(p.Unk01);
                bw.Write(p.Unk02);
                bw.Write(p.Unk03);
                bw.Write(p.Unk04);
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Flags", "0x" + Flags.Hex);
            if (ParamCount > 0)
            {
                RelXml.OpenTag(sb, indent, "Params");
                var cind = indent + 1;
                var cind2 = indent + 2;
                for (int i = 0; i < ParamCount; i++)
                {
                    var p = Params[i];
                    RelXml.OpenTag(sb, cind, "Item");
                    RelXml.StringTag(sb, cind2, "Unk01", RelXml.HashString(p.Unk01));
                    RelXml.StringTag(sb, cind2, "Unk02", RelXml.HashString(p.Unk02));
                    RelXml.StringTag(sb, cind2, "Unk03", RelXml.HashString(p.Unk03));
                    RelXml.StringTag(sb, cind2, "Unk04", RelXml.HashString(p.Unk04));
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Params");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Params");
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Flags = Xml.GetChildUIntAttribute(node, "Flags", "value");
            var vnode = node.SelectSingleNode("Params");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<Param>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new Param();
                        v.Unk01 = XmlRel.GetHash(Xml.GetChildInnerText(inode, "Unk01"));
                        v.Unk02 = XmlRel.GetHash(Xml.GetChildInnerText(inode, "Unk02"));
                        v.Unk03 = XmlRel.GetHash(Xml.GetChildInnerText(inode, "Unk03"));
                        v.Unk04 = XmlRel.GetHash(Xml.GetChildInnerText(inode, "Unk04"));
                        vlist.Add(v);
                    }
                    ParamCount = vlist.Count;
                    Params = vlist.ToArray();
                }
            }
        }
    }
    [TC(typeof(EXP))] public class Dat151Weapon : Dat151RelData
    {
        public FlagsUint Flags { get; set; }
        public MetaHash Unk01 { get; set; }
        public MetaHash Unk02 { get; set; }
        public MetaHash Unk03 { get; set; }
        public MetaHash Unk04 { get; set; }
        public float Unk05 { get; set; }
        public int Unk06 { get; set; }
        public float Unk07 { get; set; }
        public MetaHash Unk08 { get; set; }
        public MetaHash Unk09 { get; set; }
        public MetaHash Unk10 { get; set; }
        public MetaHash Unk11 { get; set; }
        public MetaHash Unk12 { get; set; }
        public MetaHash Unk13 { get; set; }//eg 0xBB0A8AE1
        public MetaHash Unk14 { get; set; }
        public MetaHash Unk15 { get; set; }
        public MetaHash Unk16 { get; set; }
        public MetaHash Unk17 { get; set; }
        public MetaHash Unk18 { get; set; }
        public int Unk19 { get; set; }//0,1,2
        public MetaHash Unk20 { get; set; }
        public MetaHash Unk21 { get; set; }
        public MetaHash Unk22 { get; set; }
        public MetaHash Unk23 { get; set; }
        public MetaHash Unk24 { get; set; }
        public MetaHash Unk25 { get; set; }
        public MetaHash Unk26 { get; set; }
        public MetaHash Unk27 { get; set; }
        public int Unk28 { get; set; }//0,50
        public int Unk29 { get; set; }//0
        public MetaHash Unk30 { get; set; }
        public MetaHash Unk31 { get; set; }
        public MetaHash Unk32 { get; set; }
        public MetaHash Unk33 { get; set; }
        public MetaHash Unk34 { get; set; }

        public MetaHash Unk35 { get; set; }
        public MetaHash Unk36 { get; set; }
        public MetaHash Unk37 { get; set; }
        public MetaHash Unk38 { get; set; }
        public MetaHash Unk39 { get; set; }
        public MetaHash Unk40 { get; set; }
        public MetaHash Unk41 { get; set; }
        public MetaHash Unk42 { get; set; }
        public MetaHash Unk43 { get; set; }
        public MetaHash Unk44 { get; set; }
        public MetaHash Unk45 { get; set; }
        public MetaHash Unk46 { get; set; }
        public MetaHash Unk47 { get; set; }

        public int Unk48 { get; set; }
        public int Unk49 { get; set; }
        public int Unk50 { get; set; }

        public MetaHash Unk51 { get; set; }
        public MetaHash Unk52 { get; set; }
        public MetaHash Unk53 { get; set; }
        public MetaHash Unk54 { get; set; }
        public MetaHash Unk55 { get; set; }
        public MetaHash Unk56 { get; set; }
        public MetaHash Unk57 { get; set; }
        public MetaHash Unk58 { get; set; }
        public MetaHash Unk59 { get; set; }
        public MetaHash Unk60 { get; set; }
        public MetaHash Unk61 { get; set; }
        public MetaHash Unk62 { get; set; }
        public int Unk63 { get; set; }
        public int Unk64 { get; set; }
        public int Unk65 { get; set; }
        public int Unk66 { get; set; }
        public int Unk67 { get; set; }

        public int Version { get; set; }


        public Dat151Weapon(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Weapon;
            TypeID = (byte)Type;
        }
        public Dat151Weapon(RelData d, BinaryReader br) : base(d, br)
        {
            Flags = br.ReadUInt32();
            Unk01 = br.ReadUInt32();
            Unk02 = br.ReadUInt32();
            Unk03 = br.ReadUInt32();
            Unk04 = br.ReadUInt32();
            Unk05 = br.ReadSingle();
            Unk06 = br.ReadInt32();
            Unk07 = br.ReadSingle();
            Unk08 = br.ReadUInt32();
            Unk09 = br.ReadUInt32();
            Unk10 = br.ReadUInt32();
            Unk11 = br.ReadUInt32();
            Unk12 = br.ReadUInt32();
            Unk13 = br.ReadUInt32();//eg 0xBB0A8AE1
            Unk14 = br.ReadUInt32();
            Unk15 = br.ReadUInt32();
            Unk16 = br.ReadUInt32();
            Unk17 = br.ReadUInt32();
            Unk18 = br.ReadUInt32();
            Unk19 = br.ReadInt32();//0,1,2
            Unk20 = br.ReadUInt32();
            Unk21 = br.ReadUInt32();
            Unk22 = br.ReadUInt32();
            Unk23 = br.ReadUInt32();
            Unk24 = br.ReadUInt32();
            Unk25 = br.ReadUInt32();
            Unk26 = br.ReadUInt32();
            Unk27 = br.ReadUInt32();
            Unk28 = br.ReadInt32();//0,50
            Unk29 = br.ReadInt32();//0
            Unk30 = br.ReadUInt32();
            Unk31 = br.ReadUInt32();
            Unk32 = br.ReadUInt32();
            Unk33 = br.ReadUInt32();
            Unk34 = br.ReadUInt32();

            Version = 0;

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            switch (bytesleft)
            {
                case 0:
                    break;
                case 52:
                case 64:
                case 132:
                    Version = 1;
                    Unk35 = br.ReadUInt32();
                    Unk36 = br.ReadUInt32();
                    Unk37 = br.ReadUInt32();
                    Unk38 = br.ReadUInt32();
                    Unk39 = br.ReadUInt32();
                    Unk40 = br.ReadUInt32();
                    Unk41 = br.ReadUInt32();
                    Unk42 = br.ReadUInt32();
                    Unk43 = br.ReadUInt32();
                    Unk44 = br.ReadUInt32();
                    Unk45 = br.ReadUInt32();
                    Unk46 = br.ReadUInt32();
                    Unk47 = br.ReadUInt32();

                    if (bytesleft >= 64)
                    {
                        Version = 2;
                        Unk48 = br.ReadInt32();
                        Unk49 = br.ReadInt32();
                        Unk50 = br.ReadInt32();

                        if (Unk48 != 0)
                        { }//only rarely hit!
                        if (Unk49 != 0)
                        { }//no hit
                        if (Unk50 != 0)
                        { }//no hit

                        if (bytesleft >= 132)
                        {
                            Version = 3;
                            Unk51 = br.ReadUInt32();
                            Unk52 = br.ReadUInt32();
                            Unk53 = br.ReadUInt32();
                            Unk54 = br.ReadUInt32();
                            Unk55 = br.ReadUInt32();
                            Unk56 = br.ReadUInt32();
                            Unk57 = br.ReadUInt32();
                            Unk58 = br.ReadUInt32();
                            Unk59 = br.ReadUInt32();
                            Unk60 = br.ReadUInt32();
                            Unk61 = br.ReadUInt32();
                            Unk62 = br.ReadUInt32();
                            Unk63 = br.ReadInt32();
                            Unk64 = br.ReadInt32();
                            Unk65 = br.ReadInt32();
                            Unk66 = br.ReadInt32();
                            Unk67 = br.ReadInt32();

                            if (bytesleft > 132)
                            { }//shouldn't get here
                        }
                    }
                    break;
                default:
                    break;
            }
            bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }

            if (Unk29 != 0)
            { }//no hit


        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Flags);
            bw.Write(Unk01);
            bw.Write(Unk02);
            bw.Write(Unk03);
            bw.Write(Unk04);
            bw.Write(Unk05);
            bw.Write(Unk06);
            bw.Write(Unk07);
            bw.Write(Unk08);
            bw.Write(Unk09);
            bw.Write(Unk10);
            bw.Write(Unk11);
            bw.Write(Unk12);
            bw.Write(Unk13);//eg 0xBB0A8AE1
            bw.Write(Unk14);
            bw.Write(Unk15);
            bw.Write(Unk16);
            bw.Write(Unk17);
            bw.Write(Unk18);
            bw.Write(Unk19);//0,1,2
            bw.Write(Unk20);
            bw.Write(Unk21);
            bw.Write(Unk22);
            bw.Write(Unk23);
            bw.Write(Unk24);
            bw.Write(Unk25);
            bw.Write(Unk26);
            bw.Write(Unk27);
            bw.Write(Unk28);//0,50
            bw.Write(Unk29);//0
            bw.Write(Unk30);
            bw.Write(Unk31);
            bw.Write(Unk32);
            bw.Write(Unk33);
            bw.Write(Unk34);

            if (Version >= 1)
            {
                bw.Write(Unk35);
                bw.Write(Unk36);
                bw.Write(Unk37);
                bw.Write(Unk38);
                bw.Write(Unk39);
                bw.Write(Unk40);
                bw.Write(Unk41);
                bw.Write(Unk42);
                bw.Write(Unk43);
                bw.Write(Unk44);
                bw.Write(Unk45);
                bw.Write(Unk46);
                bw.Write(Unk47);

                if (Version >= 2)
                {
                    bw.Write(Unk48);
                    bw.Write(Unk49);
                    bw.Write(Unk50);

                    if (Version >= 3)
                    {
                        bw.Write(Unk51);
                        bw.Write(Unk52);
                        bw.Write(Unk53);
                        bw.Write(Unk54);
                        bw.Write(Unk55);
                        bw.Write(Unk56);
                        bw.Write(Unk57);
                        bw.Write(Unk58);
                        bw.Write(Unk59);
                        bw.Write(Unk60);
                        bw.Write(Unk61);
                        bw.Write(Unk62);
                        bw.Write(Unk63);
                        bw.Write(Unk64);
                        bw.Write(Unk65);
                        bw.Write(Unk66);
                        bw.Write(Unk67);
                    }
                }
            }

        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Flags", "0x" + Flags.Hex);
            RelXml.ValueTag(sb, indent, "Version", Version.ToString()); //CW invention, not an actual field!
            RelXml.StringTag(sb, indent, "Unk01", RelXml.HashString(Unk01));
            RelXml.StringTag(sb, indent, "Unk02", RelXml.HashString(Unk02));
            RelXml.StringTag(sb, indent, "Unk03", RelXml.HashString(Unk03));
            RelXml.StringTag(sb, indent, "Unk04", RelXml.HashString(Unk04));
            RelXml.ValueTag(sb, indent, "Unk05", FloatUtil.ToString(Unk05));
            RelXml.ValueTag(sb, indent, "Unk06", Unk06.ToString());
            RelXml.ValueTag(sb, indent, "Unk07", FloatUtil.ToString(Unk07));
            RelXml.StringTag(sb, indent, "Unk08", RelXml.HashString(Unk08));
            RelXml.StringTag(sb, indent, "Unk09", RelXml.HashString(Unk09));
            RelXml.StringTag(sb, indent, "Unk10", RelXml.HashString(Unk10));
            RelXml.StringTag(sb, indent, "Unk11", RelXml.HashString(Unk11));
            RelXml.StringTag(sb, indent, "Unk12", RelXml.HashString(Unk12));
            RelXml.StringTag(sb, indent, "Unk13", RelXml.HashString(Unk13));
            RelXml.StringTag(sb, indent, "Unk14", RelXml.HashString(Unk14));
            RelXml.StringTag(sb, indent, "Unk15", RelXml.HashString(Unk15));
            RelXml.StringTag(sb, indent, "Unk16", RelXml.HashString(Unk16));
            RelXml.StringTag(sb, indent, "Unk17", RelXml.HashString(Unk17));
            RelXml.StringTag(sb, indent, "Unk18", RelXml.HashString(Unk18));
            RelXml.ValueTag(sb, indent, "Unk19", Unk19.ToString());
            RelXml.StringTag(sb, indent, "Unk20", RelXml.HashString(Unk20));
            RelXml.StringTag(sb, indent, "Unk21", RelXml.HashString(Unk21));
            RelXml.StringTag(sb, indent, "Unk22", RelXml.HashString(Unk22));
            RelXml.StringTag(sb, indent, "Unk23", RelXml.HashString(Unk23));
            RelXml.StringTag(sb, indent, "Unk24", RelXml.HashString(Unk24));
            RelXml.StringTag(sb, indent, "Unk25", RelXml.HashString(Unk25));
            RelXml.StringTag(sb, indent, "Unk26", RelXml.HashString(Unk26));
            RelXml.StringTag(sb, indent, "Unk27", RelXml.HashString(Unk27));
            RelXml.ValueTag(sb, indent, "Unk28", Unk28.ToString());
            RelXml.ValueTag(sb, indent, "Unk29", Unk29.ToString());
            RelXml.StringTag(sb, indent, "Unk30", RelXml.HashString(Unk30));
            RelXml.StringTag(sb, indent, "Unk31", RelXml.HashString(Unk31));
            RelXml.StringTag(sb, indent, "Unk32", RelXml.HashString(Unk32));
            RelXml.StringTag(sb, indent, "Unk33", RelXml.HashString(Unk33));
            RelXml.StringTag(sb, indent, "Unk34", RelXml.HashString(Unk34));

            if (Version >= 1)
            {
                RelXml.StringTag(sb, indent, "Unk35", RelXml.HashString(Unk35));
                RelXml.StringTag(sb, indent, "Unk36", RelXml.HashString(Unk36));
                RelXml.StringTag(sb, indent, "Unk37", RelXml.HashString(Unk37));
                RelXml.StringTag(sb, indent, "Unk38", RelXml.HashString(Unk38));
                RelXml.StringTag(sb, indent, "Unk39", RelXml.HashString(Unk39));
                RelXml.StringTag(sb, indent, "Unk40", RelXml.HashString(Unk40));
                RelXml.StringTag(sb, indent, "Unk41", RelXml.HashString(Unk41));
                RelXml.StringTag(sb, indent, "Unk42", RelXml.HashString(Unk42));
                RelXml.StringTag(sb, indent, "Unk43", RelXml.HashString(Unk43));
                RelXml.StringTag(sb, indent, "Unk44", RelXml.HashString(Unk44));
                RelXml.StringTag(sb, indent, "Unk45", RelXml.HashString(Unk45));
                RelXml.StringTag(sb, indent, "Unk46", RelXml.HashString(Unk46));
                RelXml.StringTag(sb, indent, "Unk47", RelXml.HashString(Unk47));

                if (Version >= 2)
                {
                    RelXml.ValueTag(sb, indent, "Unk48", Unk48.ToString());
                    RelXml.ValueTag(sb, indent, "Unk49", Unk49.ToString());
                    RelXml.ValueTag(sb, indent, "Unk50", Unk50.ToString());

                    if (Version >= 3)
                    {
                        RelXml.StringTag(sb, indent, "Unk51", RelXml.HashString(Unk51));
                        RelXml.StringTag(sb, indent, "Unk52", RelXml.HashString(Unk52));
                        RelXml.StringTag(sb, indent, "Unk53", RelXml.HashString(Unk53));
                        RelXml.StringTag(sb, indent, "Unk54", RelXml.HashString(Unk54));
                        RelXml.StringTag(sb, indent, "Unk55", RelXml.HashString(Unk55));
                        RelXml.StringTag(sb, indent, "Unk56", RelXml.HashString(Unk56));
                        RelXml.StringTag(sb, indent, "Unk57", RelXml.HashString(Unk57));
                        RelXml.StringTag(sb, indent, "Unk58", RelXml.HashString(Unk58));
                        RelXml.StringTag(sb, indent, "Unk59", RelXml.HashString(Unk59));
                        RelXml.StringTag(sb, indent, "Unk60", RelXml.HashString(Unk60));
                        RelXml.StringTag(sb, indent, "Unk61", RelXml.HashString(Unk61));
                        RelXml.StringTag(sb, indent, "Unk62", RelXml.HashString(Unk62));
                        RelXml.ValueTag(sb, indent, "Unk63", Unk63.ToString());
                        RelXml.ValueTag(sb, indent, "Unk64", Unk64.ToString());
                        RelXml.ValueTag(sb, indent, "Unk65", Unk65.ToString());
                        RelXml.ValueTag(sb, indent, "Unk66", Unk66.ToString());
                        RelXml.ValueTag(sb, indent, "Unk67", Unk67.ToString());
                    }
                }
            }
        }
        public override void ReadXml(XmlNode node)
        {
            Flags = Xml.GetChildUIntAttribute(node, "Flags", "value");
            Version = Xml.GetChildIntAttribute(node, "Version", "value");
            Unk01 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk01"));
            Unk02 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk02"));
            Unk03 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk03"));
            Unk04 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk04"));
            Unk05 = Xml.GetChildFloatAttribute(node, "Unk05", "value");
            Unk06 = Xml.GetChildIntAttribute(node, "Unk06", "value");
            Unk07 = Xml.GetChildFloatAttribute(node, "Unk07", "value");
            Unk08 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk08"));
            Unk09 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk09"));
            Unk10 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk10"));
            Unk11 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk11"));
            Unk12 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk12"));
            Unk13 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk13"));
            Unk14 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk14"));
            Unk15 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk15"));
            Unk16 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk16"));
            Unk17 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk17"));
            Unk18 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk18"));
            Unk19 = Xml.GetChildIntAttribute(node, "Unk19", "value");
            Unk20 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk20"));
            Unk21 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk21"));
            Unk22 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk22"));
            Unk23 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk23"));
            Unk24 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk24"));
            Unk25 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk25"));
            Unk26 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk26"));
            Unk27 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk27"));
            Unk28 = Xml.GetChildIntAttribute(node, "Unk28", "value");
            Unk29 = Xml.GetChildIntAttribute(node, "Unk29", "value");
            Unk30 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk30"));
            Unk31 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk31"));
            Unk32 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk32"));
            Unk33 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk33"));
            Unk34 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk34"));

            if (Version >= 1)
            {
                Unk35 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk35"));
                Unk36 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk36"));
                Unk37 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk37"));
                Unk38 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk38"));
                Unk39 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk39"));
                Unk40 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk40"));
                Unk41 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk41"));
                Unk42 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk42"));
                Unk43 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk43"));
                Unk44 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk44"));
                Unk45 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk45"));
                Unk46 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk46"));
                Unk47 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk47"));

                if (Version >= 2)
                {
                    Unk48 = Xml.GetChildIntAttribute(node, "Unk48", "value");
                    Unk49 = Xml.GetChildIntAttribute(node, "Unk49", "value");
                    Unk50 = Xml.GetChildIntAttribute(node, "Unk50", "value");

                    if (Version >= 3)
                    {
                        Unk51 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk51"));
                        Unk52 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk52"));
                        Unk53 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk53"));
                        Unk54 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk54"));
                        Unk55 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk55"));
                        Unk56 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk56"));
                        Unk57 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk57"));
                        Unk58 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk58"));
                        Unk59 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk59"));
                        Unk60 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk60"));
                        Unk61 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk61"));
                        Unk62 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk62"));
                        Unk63 = Xml.GetChildIntAttribute(node, "Unk63", "value");
                        Unk64 = Xml.GetChildIntAttribute(node, "Unk64", "value");
                        Unk65 = Xml.GetChildIntAttribute(node, "Unk65", "value");
                        Unk66 = Xml.GetChildIntAttribute(node, "Unk66", "value");
                        Unk67 = Xml.GetChildIntAttribute(node, "Unk67", "value");
                    }
                }
            }
        }
    }
    [TC(typeof(EXP))] public class Dat151Explosion : Dat151RelData
    {
        public FlagsUint Unk01 { get; set; }
        public MetaHash Unk02 { get; set; }
        public MetaHash Unk03 { get; set; }
        public float Unk04 { get; set; }
        public float Unk05 { get; set; }
        public float Unk06 { get; set; }
        public float Unk07 { get; set; }
        public float Unk08 { get; set; }
        public MetaHash Unk09 { get; set; }
        public MetaHash Unk10 { get; set; }
        public int Unk11 { get; set; }
        public int Unk12 { get; set; }

        public Dat151Explosion(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Explosion;
            TypeID = (byte)Type;
        }
        public Dat151Explosion(RelData d, BinaryReader br) : base(d, br)
        {
            Unk01 = br.ReadUInt32();//flags
            Unk02 = br.ReadUInt32();//hash
            Unk03 = br.ReadUInt32();//hash
            Unk04 = br.ReadSingle();//float
            Unk05 = br.ReadSingle();//float
            Unk06 = br.ReadSingle();//float
            Unk07 = br.ReadSingle();//float
            Unk08 = br.ReadSingle();//float
            Unk09 = br.ReadUInt32();//hash
            Unk10 = br.ReadUInt32();//hash
            Unk11 = br.ReadInt32();//0
            Unk12 = br.ReadInt32();//0

            if (Unk11 != 0)
            { }
            if (Unk12 != 0)
            { }

            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk01);//flags
            bw.Write(Unk02);//hash
            bw.Write(Unk03);//hash
            bw.Write(Unk04);//float
            bw.Write(Unk05);//float
            bw.Write(Unk06);//float
            bw.Write(Unk07);//float
            bw.Write(Unk08);//float
            bw.Write(Unk09);//hash
            bw.Write(Unk10);//hash
            bw.Write(Unk11);//0
            bw.Write(Unk12);//0
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk01", "0x" + Unk01.Hex);
            RelXml.StringTag(sb, indent, "Unk02", RelXml.HashString(Unk02));
            RelXml.StringTag(sb, indent, "Unk03", RelXml.HashString(Unk03));
            RelXml.ValueTag(sb, indent, "Unk04", FloatUtil.ToString(Unk04));
            RelXml.ValueTag(sb, indent, "Unk05", FloatUtil.ToString(Unk05));
            RelXml.ValueTag(sb, indent, "Unk06", FloatUtil.ToString(Unk06));
            RelXml.ValueTag(sb, indent, "Unk07", FloatUtil.ToString(Unk07));
            RelXml.ValueTag(sb, indent, "Unk08", FloatUtil.ToString(Unk08));
            RelXml.StringTag(sb, indent, "Unk09", RelXml.HashString(Unk09));
            RelXml.StringTag(sb, indent, "Unk10", RelXml.HashString(Unk10));
            RelXml.ValueTag(sb, indent, "Unk11", Unk11.ToString());
            RelXml.ValueTag(sb, indent, "Unk12", Unk12.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            Unk01 = Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk02"));
            Unk03 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk03"));
            Unk04 = Xml.GetChildFloatAttribute(node, "Unk04", "value");
            Unk05 = Xml.GetChildFloatAttribute(node, "Unk05", "value");
            Unk06 = Xml.GetChildFloatAttribute(node, "Unk06", "value");
            Unk07 = Xml.GetChildFloatAttribute(node, "Unk07", "value");
            Unk08 = Xml.GetChildFloatAttribute(node, "Unk08", "value");
            Unk09 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk09"));
            Unk10 = XmlRel.GetHash(Xml.GetChildInnerText(node, "Unk10"));
            Unk11 = Xml.GetChildIntAttribute(node, "Unk11", "value");
            Unk12 = Xml.GetChildIntAttribute(node, "Unk12", "value");
        }
    }
    [TC(typeof(EXP))] public class Dat151PedPVG : Dat151RelData
    {
        public FlagsUint Unk00 { get; set; }//flags?
        public byte Unk01 { get; set; } = 94;
        public byte Unk02 { get; set; } = 57;
        public byte Unk03 { get; set; } = 245;
        public byte ItemCount1 { get; set; }
        public UnkStruct[] Items1 { get; set; }
        public byte ItemCount2 { get; set; }
        public UnkStruct[] Items2 { get; set; }
        public byte ItemCount3 { get; set; }
        public UnkStruct[] Items3 { get; set; }
        public byte Unk07 { get; set; } //item count4? (=0)


        public class UnkStruct
        {
            public MetaHash Name { get; set; }
            public FlagsUint Unk1 { get; set; }
            public FlagsUint Unk2 { get; set; }

            public UnkStruct()
            { }
            public UnkStruct(BinaryReader br)
            {
                Name = br.ReadUInt32();
                Unk1 = br.ReadUInt32();
                Unk2 = br.ReadUInt32();

                if (Unk1 != 0)
                { }//no hit
                if (Unk2 != 0)
                { }//no hit
            }
            public void Write(BinaryWriter bw)
            {
                bw.Write(Name);
                bw.Write(Unk1);
                bw.Write(Unk2);
            }
            public void WriteXml(StringBuilder sb, int indent)
            {
                RelXml.StringTag(sb, indent, "Name", RelXml.HashString(Name));
                RelXml.ValueTag(sb, indent, "Unk1", Unk1.Value.ToString());
                RelXml.ValueTag(sb, indent, "Unk2", Unk2.Value.ToString());
            }
            public void ReadXml(XmlNode node)
            {
                Name = XmlRel.GetHash(Xml.GetChildInnerText(node, "Name"));
                Unk1 = Xml.GetChildUIntAttribute(node, "Unk1", "value");
                Unk2 = Xml.GetChildUIntAttribute(node, "Unk2", "value");
            }
            public override string ToString()
            {
                return Name.ToString() + ", " + Unk1.Value.ToString() + ", " + Unk2.Value.ToString();
            }
        }

        public Dat151PedPVG(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.PedPVG;
            TypeID = (byte)Type;
        }
        public Dat151PedPVG(RelData d, BinaryReader br) : base(d, br)
        {
            Unk00 = br.ReadUInt32();//flags?
            Unk01 = br.ReadByte();//94
            Unk02 = br.ReadByte();//57
            Unk03 = br.ReadByte();//245

            ItemCount1 = br.ReadByte();
            Items1 = new UnkStruct[ItemCount1];
            for (int i = 0; i < ItemCount1; i++)
            {
                Items1[i] = new UnkStruct(br);
            }

            ItemCount2 = br.ReadByte();
            Items2 = new UnkStruct[ItemCount2];
            for (int i = 0; i < ItemCount2; i++)
            {
                Items2[i] = new UnkStruct(br);
            }

            ItemCount3 = br.ReadByte();
            Items3 = new UnkStruct[ItemCount3];
            for (int i = 0; i < ItemCount3; i++)
            {
                Items3[i] = new UnkStruct(br);
            }

            Unk07 = br.ReadByte();
            //Items4 = new UnkStruct[Unk07];
            //for (int i = 0; i < Unk07; i++)
            //{
            //    Items4[i] = new UnkStruct(br);
            //}



            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
            //if (Unk06 != 0)
            //{ }
            //if (Unk04 != 0)
            //{ }

        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);

            bw.Write(Unk00);//flags?
            bw.Write(Unk01);//94
            bw.Write(Unk02);//57
            bw.Write(Unk03);//245
            bw.Write(ItemCount1);
            for (int i = 0; i < ItemCount1; i++)
            {
                Items1[i].Write(bw);
            }
            bw.Write(ItemCount2);
            for (int i = 0; i < ItemCount2; i++)
            {
                Items2[i].Write(bw);
            }
            bw.Write(ItemCount3);
            for (int i = 0; i < ItemCount3; i++)
            {
                Items3[i].Write(bw);
            }
            bw.Write(Unk07);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
            RelXml.ValueTag(sb, indent, "Unk00", "0x" + Unk00.Hex);
            RelXml.ValueTag(sb, indent, "Unk01", Unk01.ToString());
            RelXml.ValueTag(sb, indent, "Unk02", Unk02.ToString());
            RelXml.ValueTag(sb, indent, "Unk03", Unk03.ToString());

            var cind = indent + 1;
            var cind2 = indent + 2;
            if (ItemCount1 > 0)
            {
                RelXml.OpenTag(sb, indent, "Items1");
                for (int i = 0; i < ItemCount1; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Items1[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Items1");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items1");
            }

            if (ItemCount2 > 0)
            {
                RelXml.OpenTag(sb, indent, "Items2");
                for (int i = 0; i < ItemCount2; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Items2[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Items2");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items2");
            }

            if (ItemCount3 > 0)
            {
                RelXml.OpenTag(sb, indent, "Items3");
                for (int i = 0; i < ItemCount3; i++)
                {
                    RelXml.OpenTag(sb, cind, "Item");
                    Items3[i].WriteXml(sb, cind2);
                    RelXml.CloseTag(sb, cind, "Item");
                }
                RelXml.CloseTag(sb, indent, "Items3");
            }
            else
            {
                RelXml.SelfClosingTag(sb, indent, "Items3");
            }

            RelXml.ValueTag(sb, indent, "Unk07", Unk07.ToString());

        }
        public override void ReadXml(XmlNode node)
        {
            Unk00 = Xml.GetChildUIntAttribute(node, "Unk00", "value");
            Unk01 = (byte)Xml.GetChildUIntAttribute(node, "Unk01", "value");
            Unk02 = (byte)Xml.GetChildUIntAttribute(node, "Unk02", "value");
            Unk03 = (byte)Xml.GetChildUIntAttribute(node, "Unk03", "value");
            var vnode = node.SelectSingleNode("Items1");
            if (vnode != null)
            {
                var inodes = vnode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<UnkStruct>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new UnkStruct();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ItemCount1 = (byte)vlist.Count;
                    Items1 = vlist.ToArray();
                }
            }
            var vnode2 = node.SelectSingleNode("Items2");
            if (vnode2 != null)
            {
                var inodes = vnode2.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<UnkStruct>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new UnkStruct();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ItemCount2 = (byte)vlist.Count;
                    Items2 = vlist.ToArray();
                }
            }
            var vnode3 = node.SelectSingleNode("Items3");
            if (vnode3 != null)
            {
                var inodes = vnode3.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var vlist = new List<UnkStruct>();
                    foreach (XmlNode inode in inodes)
                    {
                        var v = new UnkStruct();
                        v.ReadXml(inode);
                        vlist.Add(v);
                    }
                    ItemCount3 = (byte)vlist.Count;
                    Items3 = vlist.ToArray();
                }
            }
            Unk07 = (byte)Xml.GetChildUIntAttribute(node, "Unk07", "value");
        }
    }










    //TODO classes
    [TC(typeof(EXP))] public class Dat151Unk2 : Dat151RelData
    {
        public Dat151Unk2(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk2;
            TypeID = (byte)Type;
        }
        public Dat151Unk2(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk7 : Dat151RelData
    {
        public Dat151Unk7(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk7;
            TypeID = (byte)Type;
        }
        public Dat151Unk7(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Helicopter : Dat151RelData
    {
        public Dat151Helicopter(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Helicopter;
            TypeID = (byte)Type;
        }
        public Dat151Helicopter(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk9 : Dat151RelData
    {
        public Dat151Unk9(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk9;
            TypeID = (byte)Type;
        }
        public Dat151Unk9(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk11 : Dat151RelData
    {
        public Dat151Unk11(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk11;
            TypeID = (byte)Type;
        }
        public Dat151Unk11(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk12 : Dat151RelData
    {
        public Dat151Unk12(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk12;
            TypeID = (byte)Type;
        }
        public Dat151Unk12(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk13 : Dat151RelData
    {
        public Dat151Unk13(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk13;
            TypeID = (byte)Type;
        }
        public Dat151Unk13(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151SpeechParams : Dat151RelData
    {
        public Dat151SpeechParams(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.SpeechParams;
            TypeID = (byte)Type;
        }
        public Dat151SpeechParams(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk15 : Dat151RelData
    {
        public Dat151Unk15(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk15;
            TypeID = (byte)Type;
        }
        public Dat151Unk15(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk16 : Dat151RelData
    {
        public Dat151Unk16(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk16;
            TypeID = (byte)Type;
        }
        public Dat151Unk16(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk18 : Dat151RelData
    {
        public Dat151Unk18(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk18;
            TypeID = (byte)Type;
        }
        public Dat151Unk18(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk22 : Dat151RelData
    {
        public Dat151Unk22(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk22;
            TypeID = (byte)Type;
        }
        public Dat151Unk22(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk23 : Dat151RelData
    {
        public Dat151Unk23(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk23;
            TypeID = (byte)Type;
        }
        public Dat151Unk23(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk27 : Dat151RelData
    {
        public Dat151Unk27(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk27;
            TypeID = (byte)Type;
        }
        public Dat151Unk27(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk28 : Dat151RelData
    {
        public Dat151Unk28(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk28;
            TypeID = (byte)Type;
        }
        public Dat151Unk28(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk29 : Dat151RelData
    {
        public Dat151Unk29(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk29;
            TypeID = (byte)Type;
        }
        public Dat151Unk29(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk31 : Dat151RelData
    {
        public Dat151Unk31(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk31;
            TypeID = (byte)Type;
        }
        public Dat151Unk31(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk33 : Dat151RelData
    {
        public Dat151Unk33(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk33;
            TypeID = (byte)Type;
        }
        public Dat151Unk33(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk35 : Dat151RelData
    {
        public Dat151Unk35(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk35;
            TypeID = (byte)Type;
        }
        public Dat151Unk35(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk36 : Dat151RelData
    {
        public Dat151Unk36(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk36;
            TypeID = (byte)Type;
        }
        public Dat151Unk36(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk40 : Dat151RelData
    {
        public Dat151Unk40(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk40;
            TypeID = (byte)Type;
        }
        public Dat151Unk40(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk41 : Dat151RelData
    {
        public Dat151Unk41(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk41;
            TypeID = (byte)Type;
        }
        public Dat151Unk41(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk42 : Dat151RelData
    {
        public Dat151Unk42(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk42;
            TypeID = (byte)Type;
        }
        public Dat151Unk42(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk45 : Dat151RelData
    {
        public Dat151Unk45(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk45;
            TypeID = (byte)Type;
        }
        public Dat151Unk45(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk48 : Dat151RelData
    {
        public Dat151Unk48(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk48;
            TypeID = (byte)Type;
        }
        public Dat151Unk48(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk51 : Dat151RelData
    {
        public Dat151Unk51(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk51;
            TypeID = (byte)Type;
        }
        public Dat151Unk51(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk53 : Dat151RelData
    {
        public Dat151Unk53(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk53;
            TypeID = (byte)Type;
        }
        public Dat151Unk53(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk54 : Dat151RelData
    {
        public Dat151Unk54(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk54;
            TypeID = (byte)Type;
        }
        public Dat151Unk54(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk56 : Dat151RelData
    {
        public Dat151Unk56(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk56;
            TypeID = (byte)Type;
        }
        public Dat151Unk56(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Aeroplane : Dat151RelData
    {
        public Dat151Aeroplane(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Aeroplane;
            TypeID = (byte)Type;
        }
        public Dat151Aeroplane(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk59 : Dat151RelData
    {
        public Dat151Unk59(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk59;
            TypeID = (byte)Type;
        }
        public Dat151Unk59(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk69 : Dat151RelData
    {
        public Dat151Unk69(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk69;
            TypeID = (byte)Type;
        }
        public Dat151Unk69(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk70 : Dat151RelData
    {
        public Dat151Unk70(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk70;
            TypeID = (byte)Type;
        }
        public Dat151Unk70(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk71 : Dat151RelData
    {
        public Dat151Unk71(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk71;
            TypeID = (byte)Type;
        }
        public Dat151Unk71(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk72 : Dat151RelData
    {
        public Dat151Unk72(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk72;
            TypeID = (byte)Type;
        }
        public Dat151Unk72(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151AnimalParams : Dat151RelData
    {
        public Dat151AnimalParams(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.AnimalParams;
            TypeID = (byte)Type;
        }
        public Dat151AnimalParams(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk74 : Dat151RelData
    {
        public Dat151Unk74(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk74;
            TypeID = (byte)Type;
        }
        public Dat151Unk74(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk75 : Dat151RelData
    {
        public Dat151Unk75(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk75;
            TypeID = (byte)Type;
        }
        public Dat151Unk75(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk77 : Dat151RelData
    {
        public Dat151Unk77(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk77;
            TypeID = (byte)Type;
        }
        public Dat151Unk77(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk79 : Dat151RelData
    {
        public Dat151Unk79(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk79;
            TypeID = (byte)Type;
        }
        public Dat151Unk79(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk80 : Dat151RelData
    {
        public Dat151Unk80(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk80;
            TypeID = (byte)Type;
        }
        public Dat151Unk80(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk82 : Dat151RelData
    {
        public Dat151Unk82(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk82;
            TypeID = (byte)Type;
        }
        public Dat151Unk82(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk85 : Dat151RelData
    {
        public Dat151Unk85(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk85;
            TypeID = (byte)Type;
        }
        public Dat151Unk85(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk95 : Dat151RelData
    {
        public Dat151Unk95(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk95;
            TypeID = (byte)Type;
        }
        public Dat151Unk95(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk96 : Dat151RelData
    {
        public Dat151Unk96(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk96;
            TypeID = (byte)Type;
        }
        public Dat151Unk96(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk99 : Dat151RelData
    {
        public Dat151Unk99(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk99;
            TypeID = (byte)Type;
        }
        public Dat151Unk99(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk100 : Dat151RelData
    {
        public Dat151Unk100(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk100;
            TypeID = (byte)Type;
        }
        public Dat151Unk100(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk101 : Dat151RelData
    {
        public Dat151Unk101(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk101;
            TypeID = (byte)Type;
        }
        public Dat151Unk101(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk105 : Dat151RelData
    {
        public Dat151Unk105(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk105;
            TypeID = (byte)Type;
        }
        public Dat151Unk105(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk106 : Dat151RelData
    {
        public Dat151Unk106(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk106;
            TypeID = (byte)Type;
        }
        public Dat151Unk106(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk107 : Dat151RelData
    {
        public Dat151Unk107(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk107;
            TypeID = (byte)Type;
        }
        public Dat151Unk107(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk108 : Dat151RelData
    {
        public Dat151Unk108(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk108;
            TypeID = (byte)Type;
        }
        public Dat151Unk108(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk109 : Dat151RelData
    {
        public Dat151Unk109(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk109;
            TypeID = (byte)Type;
        }
        public Dat151Unk109(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk110 : Dat151RelData
    {
        public Dat151Unk110(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk110;
            TypeID = (byte)Type;
        }
        public Dat151Unk110(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk111 : Dat151RelData
    {
        public Dat151Unk111(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk111;
            TypeID = (byte)Type;
        }
        public Dat151Unk111(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk112 : Dat151RelData
    {
        public Dat151Unk112(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk112;
            TypeID = (byte)Type;
        }
        public Dat151Unk112(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk113 : Dat151RelData
    {
        public Dat151Unk113(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk113;
            TypeID = (byte)Type;
        }
        public Dat151Unk113(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk115 : Dat151RelData
    {
        public Dat151Unk115(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk115;
            TypeID = (byte)Type;
        }
        public Dat151Unk115(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk116 : Dat151RelData
    {
        public Dat151Unk116(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk116;
            TypeID = (byte)Type;
        }
        public Dat151Unk116(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk118 : Dat151RelData
    {
        public Dat151Unk118(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk118;
            TypeID = (byte)Type;
        }
        public Dat151Unk118(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk119 : Dat151RelData
    {
        public Dat151Unk119(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk119;
            TypeID = (byte)Type;
        }
        public Dat151Unk119(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }
    [TC(typeof(EXP))] public class Dat151Unk121 : Dat151RelData
    {
        public Dat151Unk121(RelFile rel) : base(rel)
        {
            Type = Dat151RelType.Unk121;
            TypeID = (byte)Type;
        }
        public Dat151Unk121(RelData d, BinaryReader br) : base(d, br)
        {
            var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
            if (bytesleft != 0)
            { }
        }
        public override void Write(BinaryWriter bw)
        {
            WriteTypeAndOffset(bw);
        }
        public override void WriteXml(StringBuilder sb, int indent)
        {
        }
    }











    //[TC(typeof(EXP))] public class Dat151BlankTemplateItem : Dat151RelData
    //{
    //    public Dat151BlankTemplateItem(RelFile rel) : base(rel)
    //    {
    //        Type = Dat151RelType.RELTYPE;
    //        TypeID = (byte)Type;
    //    }
    //    public Dat151BlankTemplateItem(RelData d, BinaryReader br) : base(d, br)
    //    {
    //        var bytesleft = br.BaseStream.Length - br.BaseStream.Position;
    //        if (bytesleft != 0)
    //        { }
    //    }
    //    public override void Write(BinaryWriter bw)
    //    {
    //        WriteTypeAndOffset(bw);
    //    }
    //    public override void WriteXml(StringBuilder sb, int indent)
    //    {
    //    }
    //}




    #endregion






    public class RelXml : MetaXmlBase
    {

        public static string GetXml(RelFile rel)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(XmlHeader);

            if ((rel != null) && (rel.RelDatasSorted != null))
            {
                int indent = 0;
                int cindent = 1;
                var iindent = 2;
                var icindent = 3;
                var name = "Dat" + ((uint)rel.RelType).ToString();

                OpenTag(sb, indent, name);

                ValueTag(sb, cindent, "Version", rel.DataUnkVal.ToString());

                if (rel.NameTable != null)
                {
                    OpenTag(sb, cindent, "NameTable");

                    foreach (var ntval in rel.NameTable)
                    {
                        StringTag(sb, iindent, "Item", ntval);
                    }

                    CloseTag(sb, cindent, "NameTable");
                }

                OpenTag(sb, cindent, "Items");

                foreach (var item in rel.RelDatasSorted)
                {
                    var typeid = item.TypeID.ToString();
                    switch (rel.RelType)
                    {
                        case RelDatFileType.Dat54DataEntries:
                            typeid = ((Dat54SoundType)item.TypeID).ToString();
                            break;
                        case RelDatFileType.Dat149:
                        case RelDatFileType.Dat150:
                        case RelDatFileType.Dat151:
                            typeid = ((Dat151RelType)item.TypeID).ToString();
                            break;
                        default:
                            break;
                    }

                    var ntoffset = "";
                    var dat151item = item as Dat151RelData;
                    if (dat151item != null)
                    {
                        ntoffset = " ntOffset=\"" + dat151item.NameTableOffset.ToString() + "\"";
                    }

                    OpenTag(sb, iindent, "Item type=\"" + typeid + "\"" + ntoffset);

                    StringTag(sb, icindent, "Name", item.Name ?? RelXml.HashString(item.NameHash));

                    item.WriteXml(sb, icindent);

                    CloseTag(sb, iindent, "Item");
                }

                CloseTag(sb, cindent, "Items");

                CloseTag(sb, indent, name);

            }

            return sb.ToString();
        }

    }

    public class XmlRel
    {

        public static RelFile GetRel(string xml)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);
            return GetRel(doc);
        }

        public static RelFile GetRel(XmlDocument doc)
        {

            var node = doc.DocumentElement;
            var reltypestr = node.Name.Substring(3);
            var reltypeint = uint.Parse(reltypestr);
            var reltype = (RelDatFileType)reltypeint;

            switch (reltype)
            {
                case RelDatFileType.Dat4://TODO!
                case RelDatFileType.Dat54DataEntries://TODO!
                    break;// return null; //TODO
            }

            RelFile rel = new RelFile();
            rel.RelType = reltype;
            rel.DataUnkVal = Xml.GetChildUIntAttribute(node, "Version", "value");

            var ntnode = node.SelectSingleNode("NameTable");
            if (ntnode != null)
            {
                var ntstrs = new List<string>();
                var ntitems = ntnode.SelectNodes("Item");
                foreach (XmlNode ntitem in ntitems)
                {
                    ntstrs.Add(ntitem.InnerText);
                }
                rel.NameTable = ntstrs.ToArray();
                rel.NameTableCount = (uint)ntstrs.Count;
            }

            var itemsnode = node.SelectSingleNode("Items");
            if (itemsnode != null)
            {
                var itemslist = new List<RelData>();
                var items = itemsnode.SelectNodes("Item");
                foreach (XmlNode item in items)
                {
                    var ntoffset = Xml.GetUIntAttribute(item, "ntOffset");
                    var typestr = Xml.GetStringAttribute(item, "type");
                    var typeid = -1;
                    switch (reltype)
                    {
                        case RelDatFileType.Dat54DataEntries:
                            Dat54SoundType st;
                            if (Enum.TryParse(typestr, out st))
                            {
                                typeid = (int)st;
                            }
                            break;
                        case RelDatFileType.Dat149:
                        case RelDatFileType.Dat150:
                        case RelDatFileType.Dat151:
                            Dat151RelType rt;
                            if (Enum.TryParse(typestr, out rt))
                            {
                                typeid = (int)rt;
                            }
                            break;
                    }
                    if (typeid < 0)
                    {
                        if (!int.TryParse(typestr, out typeid))
                        {
                            continue;//couldn't determine type!
                        }
                    }


                    RelData rd = rel.CreateRelData(reltype, typeid);
                    rd.Name = Xml.GetChildInnerText(item, "Name");
                    rd.NameHash = XmlRel.GetHash(rd.Name);
                    rd.ReadXml(item);
                    itemslist.Add(rd);


                    var dat151data = rd as Dat151RelData;
                    if (dat151data != null)
                    {
                        dat151data.NameTableOffset = ntoffset;
                    }

                }

                rel.RelDatas = itemslist.ToArray();//this one will get sorted on save
                rel.RelDatasSorted = itemslist.ToArray();
            }


            return rel;
        }


        public static MetaHash GetHash(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return 0;
            }
            if (str.StartsWith("hash_"))
            {
                return Convert.ToUInt32(str.Substring(5), 16);
            }
            else
            {
                JenkIndex.Ensure(str);
                return JenkHash.GenHash(str);
            }
        }

    }



}
