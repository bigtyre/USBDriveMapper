﻿namespace BigTyre.DiskMount.Configuration
{
    internal class Volumes
    {
        public string Title { get; set; }
        public string VolumeName { get; set; }
        public bool IsBitLockerEncrypted { get; set; }
        public string BitLockerPassword { get; set; }
    }
}
