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
        public List<Volume> Volumes { get; set; } = new List<Volume>();
        public int CheckFrequencyInSeconds { get; set; }
        public int UnlockingAttempts { get; set; }
    }
}
