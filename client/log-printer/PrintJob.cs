
namespace log_printer
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using log_printer.Data;

    public class PrintJob
    {
        public Uri DatabaseUrl { get; set; }
        public Mission Mission { get; set; }
        public string Printer { get; set; }
    }
}
