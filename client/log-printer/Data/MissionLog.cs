using System;
using System.Collections.Generic;
using System.Text;

namespace log_printer.Data
{
    public class MissionLog
    {
        public Guid id { get; set; }
        public string message { get; set; }
        public Guid mission_id { get; set; }
        public DateTime when { get; set; }
    }
}
