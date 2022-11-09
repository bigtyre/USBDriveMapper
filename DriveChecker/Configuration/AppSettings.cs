using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BigTyre.DiskMount.Configuration
{
    internal class AppSettings
    {
        public string TargetDirectory { get; set; } = "";
        public List<string> VolumeNames { get; set; } = new List<string>();
        public int CheckFrequencyInSeconds { get; set; }
    }
}
