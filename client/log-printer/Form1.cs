namespace log_printer
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Configuration;
    using System.Drawing.Printing;
    using System.IO;
    using System.Net;
    using System.Windows.Forms;
    using log_printer.Data;
    using Newtonsoft.Json;

    public partial class frmMain : Form
    {
        private BackgroundWorker printThread = new BackgroundWorker();

        private List<Mission> missions = new List<Mission>();
        private List<MissionLog> logs = new List<MissionLog>();

        private DateTime nextPrintTime;
       

        public frmMain()
        {
            InitializeComponent();
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                comboBox1.Items.Add(printer);
            }

            PrintDocument printDoc = new PrintDocument();
            comboBox1.Text = printDoc.PrinterSettings.PrinterName;
            textBox1.Text = ConfigurationManager.AppSettings["defaultUrl"] ?? "";
         
            printThread.DoWork += printThread_DoWork;
            printThread.RunWorkerCompleted += printThread_RunWorkerCompleted;
            printThread.ProgressChanged += printThread_ProgressChanged;
            printThread.WorkerReportsProgress = true;

            UpdatePrintTime();
        }

        void StartFetchAndPrint()
        {
            button2.Enabled = false;

            PrintJob job = new PrintJob();
            job.DatabaseUrl = new Uri(textBox1.Text);
            job.Mission = (Mission)listBox1.SelectedItem;
            job.Printer = comboBox1.Text;

            if (printThread.IsBusy)
                return;

            printThread.RunWorkerAsync(job);
        }

        void printThread_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            printProgress.Value = e.ProgressPercentage;
        }

        void printThread_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            printProgress.Value = 0;
            button2.Enabled = true;
        }


        void printThread_DoWork(object sender, DoWorkEventArgs e)
        {
            new PrintTask((PrintJob)e.Argument, (BackgroundWorker)sender).Start();
        }



        private void button1_Click(object sender, EventArgs e)
        {
            button1.Enabled = false;
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(new Uri(new Uri(textBox1.Text), "missions/mostrecent/5.json"));
            request.Accept = "application/json";

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            string json = "";
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                json = reader.ReadToEnd();
            }

            missions = JsonConvert.DeserializeObject<List<Mission>>(json);
            missions.Sort(CompareMissions);
            listBox1.Items.Clear();
            listBox1.Items.AddRange(missions.ToArray());
            if (missions.Count > 0)
            {
                listBox1.SelectedIndex = 0;
                button2.Enabled = true;
            }

            button1.Enabled = true;            
        }

        private static int CompareMissions(Mission left, Mission right)
        {
            return -left.started.CompareTo(right.started);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            StartFetchAndPrint();
        }

        private void UpdatePrintTime()
        {
            DateTime now = DateTime.Now;
            nextPrintTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes((int)printIntervalBox.Value);
            
            nextPrintLabel.Text = "Next: " + 
                (nextPrintLabel.Enabled
                    ? ((listBox1.SelectedItem == null) ? "No mission selected" : nextPrintTime.ToString("HH:mm"))
                    : "");
        }

        private void SetPrintInterval(int minutes)
        {
            //printTimer.Interval = 60 * minutes;
            UpdatePrintTime();
        }

        private void autoPrint_CheckedChanged(object sender, EventArgs e)
        {
            label4.Enabled = autoPrint.Checked;
            label5.Enabled = autoPrint.Checked;
            nextPrintLabel.Enabled = autoPrint.Checked;
            printIntervalBox.Enabled = autoPrint.Checked;
            printTimer.Enabled = autoPrint.Checked;

            if (autoPrint.Checked)
            {
                UpdatePrintTime();
            }
        }

        private void printIntervalBox_ValueChanged(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("updated");
            SetPrintInterval((int)printIntervalBox.Value);
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdatePrintTime();
        }

        private void printTimer_Tick(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("tick");
            if (DateTime.Now < nextPrintTime)
                return;

            StartFetchAndPrint();
            UpdatePrintTime();
        }
    }
}
