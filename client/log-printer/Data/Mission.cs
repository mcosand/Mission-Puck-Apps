using System;
using System.Collections.Generic;
using System.Text;

namespace log_printer.Data
{
    public class Mission
    {
        public Guid id { get; set; }
        public string title { get; set; }
        public string number { get; set; }
        public string county { get; set; }
        public DateTime started { get; set; }

        public override string ToString()
        {
            return string.Format("{0} {1}", this.number, this.title);
        }
    }
}
