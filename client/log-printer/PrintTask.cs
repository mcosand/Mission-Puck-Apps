namespace log_printer
{
    using iTextSharp.text.pdf;
    using log_printer.Data;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Drawing;
    using System.Drawing.Printing;
    using System.IO;
    using System.Net;
    using System.Reflection;
    using System.Threading;

    public class PrintTask
    {
        private PrintJob job;
        private BackgroundWorker worker;
        private PrintDocument printer;

        private Image tiffImage = null;
        private int currentTiffFrame = 0;
        private int tiffFrameCount = 0;

        private List<MissionLog> logs;

        public PrintTask(PrintJob job, BackgroundWorker worker)
        {
            this.job = job;
            this.worker = worker;
            printer = new PrintDocument();
            printer.PrintPage += printer_PrintPage;
            printer.PrinterSettings.PrinterName = job.Printer;
            if (printer.PrinterSettings.CanDuplex) printer.PrinterSettings.Duplex = Duplex.Horizontal;
        }

        public void Start()
        {
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(new Uri(job.DatabaseUrl, string.Format("missions/{0}/logs.json", job.Mission.id)));
            request.Accept = "application/json";

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            string json = "";
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                json = reader.ReadToEnd();
            }

            //json = json.TrimEnd(']');
            //for (int i = 0; i < 255; i++)
            //{
            //    json += ",{\"id\":\"8b3d9e0d-19e9-44ab-9870-a24b1b971177\",\"message\":\"This is a log message\",\"mission_id\":\"bc94292c-2a63-4f9a-bfb7-669fd4121c4a\",\"when\":\"2013-03-20T22:58:47-07:00\"}";
            //}
            //json += "]";

            logs = JsonConvert.DeserializeObject<List<MissionLog>>(json);
            logs.Sort(CompareLogs);
            worker.ReportProgress(10);

            string tempPdf = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".pdf";
            string tempPng = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".tif";
            try
            {
                RenderLogs(job.Mission, logs, tempPdf, tempPng);
                worker.ReportProgress(60);
                tiffImage = Image.FromFile(tempPng);
                tiffFrameCount = tiffImage.GetFrameCount(System.Drawing.Imaging.FrameDimension.Page);
                currentTiffFrame = 0;
                printer.Print();
                worker.ReportProgress(70);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPdf)) File.Delete(tempPdf);
                }
                catch (System.IO.IOException)
                {
                    // MessageBox.Show("Failed to delete " + tempPdf);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e.Message);
                    throw;
                }
            }
        }

        /// <summary>
        /// Render the log messages into PDF template, then transform to multi-page TIFF to make printing easier.
        /// </summary>
        /// <param name="mission"></param>
        /// <param name="logs"></param>
        /// <param name="tempPdf"></param>
        /// <param name="tempPng"></param>
        private void RenderLogs(Mission mission, List<MissionLog> logs, string tempPdf, string tempPng)
        {
            DateTime? minDate = null;
            DateTime? maxDate = null;
            int startPercent = 10;
            int progressPercentSpan = 30;

            foreach (MissionLog log in logs)
            {
                if (minDate == null || minDate.Value > log.when) minDate = log.when;
                if (maxDate == null || maxDate.Value < log.when) maxDate = log.when;
            }

            string myPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string pdfTemplate = Path.Combine(myPath, "ics109-log.pdf");


            using (MemoryStream result = new MemoryStream())
            {
                iTextSharp.text.Document resultDoc = new iTextSharp.text.Document();
                PdfCopy copy = new PdfCopy(resultDoc, result);
                resultDoc.Open();

                Queue<LogEntry> rows = null;
                int numPages = -1;
                int totalRows = 0;
                int page = 1;

                List<string> operators = new List<string>();

                do
                {
                    if (numPages > 0) worker.ReportProgress(startPercent + (page * progressPercentSpan / numPages));

                    using (MemoryStream filledForm = new MemoryStream())
                    {
                        iTextSharp.text.pdf.PdfReader pdfReader = new iTextSharp.text.pdf.PdfReader(pdfTemplate);


                        BaseFont font = BaseFont.CreateFont(BaseFont.HELVETICA_BOLD, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);
                        PdfGState gState = new PdfGState();
                        gState.FillOpacity = 0.45F;
                        gState.StrokeOpacity = 0.45F;

                        using (MemoryStream buf = new MemoryStream())
                        {
                            PdfStamper stamper = new PdfStamper(pdfReader, buf);

                            // Render "DRAFT\nyyyy-MM-dd HH:mm" in the center of the page.
                            int fontSize = 64;
                            iTextSharp.text.Rectangle rect = pdfReader.GetPageSizeWithRotation(1);
                            PdfContentByte underContent = stamper.GetUnderContent(1);
                            underContent.SaveState();
                            underContent.SetGState(gState);
                            underContent.SetColorFill(iTextSharp.text.BaseColor.LIGHT_GRAY);
                            underContent.BeginText();
                            underContent.SetFontAndSize(font, fontSize);
                            underContent.SetTextMatrix(30, 30);
                            Single currentY = (rect.Height / 2) + ((fontSize * 2) / 2); // font size * rowCount / 2
                            underContent.ShowTextAligned(iTextSharp.text.Element.ALIGN_CENTER, "DRAFT", rect.Width / 2, currentY - ((0 * fontSize) + (fontSize / 4) * 0), 0F);
                            underContent.ShowTextAligned(iTextSharp.text.Element.ALIGN_CENTER, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), rect.Width / 2, currentY - ((1 * fontSize) + (fontSize / 4) * 1), 0F);
                            underContent.EndText();
                            underContent.RestoreState();

                            // Fill in the fields
                            var fields = stamper.AcroFields;
                            if (rows == null)
                            {
                                rows = Fill109Rows(logs, fields, "topmostSubform[0].Page1[0].SUBJECTRow1[0]");
                                totalRows = rows.Count;
                            }

                            foreach (var field in fields.Fields)
                            {
                                fields.SetField(field.Key, "");
                            }

                            int currentRow = 1;
                            operators.Clear();
                            while (rows.Count > 0 && fields.GetField("topmostSubform[0].Page1[0].SUBJECTRow" + currentRow.ToString() + "[0]") != null)
                            {
                                var row = rows.Dequeue();

                                fields.SetField("topmostSubform[0].Page1[0].TIMERow" + currentRow.ToString() + "[0]", row.time);
                                fields.SetField("topmostSubform[0].Page1[0].SUBJECTRow" + currentRow.ToString() + "[0]", row.text);

                                if (!operators.Contains(row.author)) operators.Add(row.author);
                                currentRow++;
                            }

                            // Now we know how many rows on a page. Figure out how many pages we need for all rows.
                            if (numPages < 0)
                            {
                                int rowsPerPage = currentRow - 1;
                                int remainder = totalRows % rowsPerPage;
                                numPages = ((remainder == 0) ? 0 : 1) + (totalRows / rowsPerPage);
                            }

                            if (numPages > 0)
                            {
                                fields.SetField("topmostSubform[0].Page1[0]._1_Incident_Name[0]", "   " + mission.title);
                                fields.SetField("topmostSubform[0].Page1[0]._3_DEM_KCSO[0]", "    " + mission.number);
                                fields.SetField("topmostSubform[0].Page1[0]._5_RADIO_OPERATOR_NAME_LOGISTICS[0]", string.Join(",", operators.ToArray()));
                                fields.SetField("topmostSubform[0].Page1[0].Text30[0]", string.Format("{0:yyyy-MM-dd}", minDate));
                                fields.SetField("topmostSubform[0].Page1[0].Text31[0]", string.Format("{0:yyyy-MM-dd}", maxDate));
                                fields.SetField("topmostSubform[0].Page1[0].Text28[0]", page.ToString());
                                fields.SetField("topmostSubform[0].Page1[0].Text29[0]", numPages.ToString());
                                fields.SetField("topmostSubform[0].Page1[0].DateTime[0]", DateTime.Now.ToString("     MMM d, yyyy  HH:mm"));
                                fields.SetField("topmostSubform[0].Page1[0]._8_Prepared_by_Name[0]", "Mission Puck");

                                fields.RemoveField("topmostSubform[0].Page1[0].PrintButton1[0]");
                            }

                            stamper.FormFlattening = false;
                            stamper.Close();

                            pdfReader = new PdfReader(buf.ToArray());
                            copy.AddPage(copy.GetImportedPage(pdfReader, 1));
                            page++;
                        }
                    }
                    //copy.Close();
                } while (rows != null && rows.Count > 0);

                resultDoc.Close();

                File.WriteAllBytes(tempPdf, result.ToArray());
            }

            ProcessStartInfo startInfo = new ProcessStartInfo(Path.Combine(myPath, "gswin32c.exe"), string.Format("-sDEVICE=tiff24nc -r400x400 -dBATCH -dNOPAUSE -sOutputFile={0} {1}", tempPng, tempPdf));
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            Process p = new Process();
            p.StartInfo = startInfo;
            p.Start();
            while (p.HasExited == false)
            {
                Thread.Sleep(1);
            }
        }

        /// <summary>Process the rows of the model, splitting them into multiple entries to make them fit in the allotted field dimensions</summary>
        /// <param name="logs"></param>
        /// <param name="fields"></param>
        /// <param name="fieldName"></param>
        /// <returns></returns>
        private static Queue<LogEntry> Fill109Rows(IEnumerable<MissionLog> logs, AcroFields fields, string fieldName)
        {
            AcroFields.Item item = fields.GetFieldItem(fieldName);
            PdfDictionary merged = item.GetMerged(0);
            TextField textField = new TextField(null, null, null);
            fields.DecodeGenericDictionary(merged, textField);

            var collection = new Queue<LogEntry>();
            float fieldWidth = fields.GetFieldPositions(fieldName)[0].position.Width;
            float padRight = textField.Font.GetWidthPoint("m", textField.FontSize);

            // For each log message
            foreach (var log in logs)
            {
                if (log.message == null) continue;

                string formTime = log.when.ToString("HHmm");
                string formOperator = "";

                // For each line in the log message
                foreach (var logMsg in log.message.Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int left = 0;
                    int right = logMsg.Length - 1;

                    // While there's more message text to put on the page ...
                    while (left < right)
                    {
                        string part = logMsg.Substring(left, right - left + 1);
                        // Keep trimming until the message segment fits in the field dimensions ...
                        while (left < right && (textField.Font.GetWidthPoint(part, textField.FontSize) + padRight) > fieldWidth)
                        {
                            right = left + part.LastIndexOf(' ');
                            part = logMsg.Substring(left, right - left);
                        }
                        collection.Enqueue(new LogEntry(formTime, part, formOperator));
                        formTime = "";
                        left = right;
                        right = logMsg.Length - 1;
                    }
                }
            }

            return collection;
        }

        /// <summary>Print each page of the output TIFF</summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void printer_PrintPage(object sender, PrintPageEventArgs e)
        {
            tiffImage.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Page, currentTiffFrame++);

            RectangleF rSourceRectangle = new RectangleF(0, 0, tiffImage.PhysicalDimension.Width, tiffImage.PhysicalDimension.Height);

            e.Graphics.DrawImage(tiffImage, printer.PrinterSettings.DefaultPageSettings.PrintableArea, rSourceRectangle, GraphicsUnit.Pixel);

            e.HasMorePages = (currentTiffFrame < tiffFrameCount);
            if (!e.HasMorePages) worker.ReportProgress(100);
        }

        /// <summary>Sort logs by time ascending</summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        private static int CompareLogs(MissionLog left, MissionLog right)
        {
            return left.when.CompareTo(right.when);
        }


        /// <summary>
        /// Stores information parsed from model and ready for insertion into PDF
        /// </summary>
        private struct LogEntry
        {
            public LogEntry(string time, string text, string author)
            {
                this.text = text;
                this.time = time;
                this.author = author;
            }
            public string text;
            public string time;
            public string author;
        }
    }
}
