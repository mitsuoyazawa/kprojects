using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NLog;
using System.Configuration;
using System.Diagnostics;
using Outlook = Microsoft.Office.Interop.Outlook;
using Microsoft.Office.Tools;

namespace PcapReceiver {
    public partial class Form1 : Form {


        private static Logger log = LogManager.GetCurrentClassLogger();


        public Form1() {
            InitializeComponent();
        }

        private void textBox4_TextChanged(object sender, EventArgs e) {

        }
        private string port = System.Configuration.ConfigurationManager.AppSettings["Port"];
        private void Form1_Load(object sender, EventArgs e) {
            backgroundWorker1.RunWorkerAsync();
            backgroundWorker4.RunWorkerAsync();
            textBox1.Text = @"C:\";
            textBox2.Text = @"file.pcap";
            textBox3.Text = @"port 5060";
            textBox6.Text = port;
            textBox7.Text = "*";
            CaptureChange();
            CurlChange();
            filelocation = textBox1.Text + textBox2.Text;
            CheckIP();
            //var folderSymbol = System.Configuration.ConfigurationSettings.AppSettings.Get("FolderSymbol");
            
        }
        private void FillListbox() {
            clearlistbox();
            DirectoryInfo di = new DirectoryInfo("C:\\");
            FileSystemInfo[] files = di.GetFileSystemInfos();
            var orderedFiles = files.OrderBy(f => f.CreationTime);
            foreach (var item in orderedFiles) {
                if (item.FullName.Contains(".pcap")) {
                    log.Debug("item:" + item.FullName);
                    if (listBox1.InvokeRequired) { 
                        listBox1.Invoke(new MethodInvoker(FillListbox), new object[] { item.FullName });
                        
                    }else
                    listBox1.Items.Add(item.FullName);
                }

            }
        }
        private void clearlistbox() {
            if (listBox1.InvokeRequired) {
                listBox1.Invoke(new MethodInvoker(FillListbox), new object[] { });

            } else
                listBox1.Items.Clear();
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e) {
            
            while (true) {
                FillListbox();

                Thread.Sleep(5000); 
            }
            
            
            //listBox1.DataSource = orderedFiles.ToList();
        }







        private static String GetBoundary(String ctype) {
            log.Debug("--" + ctype.Split(';')[1].Split('=')[1]);
            return "--" + ctype.Split(';')[1].Split('=')[1];
        }

        private static void SaveFile(Encoding enc, String boundary, Stream input) {
            Byte[] boundaryBytes = enc.GetBytes(boundary);
            Int32 boundaryLen = boundaryBytes.Length;

            log.Debug("boundaryLength: " + boundaryLen);

            using (FileStream output = new FileStream("c:\\pcap.pcap", FileMode.Create, FileAccess.Write)) {
                Byte[] buffer = new Byte[1024];
                Int32 len = input.Read(buffer, 0, 1024);
                Int32 startPos = -1;

                //log.Debug("length:" + len);

                // Find start boundary
                while (true) {
                    if (len == 0) {
                        throw new Exception("Start Boundary Not Found");
                    }

                    startPos = IndexOf(buffer, len, boundaryBytes);
                    if (startPos >= 0) {
                        break;
                    } else {
                        Array.Copy(buffer, len - boundaryLen, buffer, 0, boundaryLen);
                        len = input.Read(buffer, boundaryLen, 1024 - boundaryLen);
                    }
                }

                // Skip four lines (Boundary, Content-Disposition, Content-Type, and a blank)
                for (Int32 i = 0; i < 4; i++) {
                    while (true) {
                        if (len == 0) {
                            throw new Exception("Preamble not Found.");
                        }

                        startPos = Array.IndexOf(buffer, enc.GetBytes("\n")[0], startPos);
                        if (startPos >= 0) {
                            startPos++;
                            break;
                        } else {
                            len = input.Read(buffer, 0, 1024);
                        }
                    }
                }

                Array.Copy(buffer, startPos, buffer, 0, len - startPos);
                len = len - startPos;

                while (true) {
                    //log.Debug("line: " + buffer);
                    Int32 endPos = IndexOf(buffer, len, boundaryBytes);
                    if (endPos >= 0) {
                        if (endPos > 0) output.Write(buffer, 0, endPos);
                        break;
                    } else if (len <= boundaryLen) {
                        throw new Exception("End Boundaray Not Found");
                    } else {
                        output.Write(buffer, 0, len - boundaryLen);
                        Array.Copy(buffer, len - boundaryLen, buffer, 0, boundaryLen);
                        len = input.Read(buffer, boundaryLen, 1024 - boundaryLen) + boundaryLen;
                    }
                }
            }
        }

        private static Int32 IndexOf(Byte[] buffer, Int32 len, Byte[] boundaryBytes) {
            for (Int32 i = 0; i <= len - boundaryBytes.Length; i++) {
                Boolean match = true;
                for (Int32 j = 0; j < boundaryBytes.Length && match; j++) {
                    match = buffer[i + j] == boundaryBytes[j];
                }

                if (match) {
                    return i;
                }
            }

            return -1;
        }

        //private void ReadWriteStream(Stream readStream, Stream writeStream)
        //{

        //    int Length = 256;
        //    Byte[] buffer = new Byte[Length];
        //    int bytesRead = readStream.Read(buffer, 0, Length);
        //    // write the required bytes
        //    while (bytesRead > 0)
        //    {
        //        writeStream.Write(buffer, 0, bytesRead);
        //        bytesRead = readStream.Read(buffer, 0, Length);
        //    }
        //    readStream.Close();
        //    writeStream.Close();
        //}
        private int leng=0;
        private int lastblock = 0;
        private Stream CopyAndClose(Stream inputStream,int length) {
            leng = 0;
            int readSize = length;
            byte[] buffer = new byte[readSize];
            MemoryStream ms = new MemoryStream();
            ms.Position = 0;
            int count = inputStream.Read(buffer, 0, readSize);
            int i = 0;
            while (count > 0) {
                leng += count;
                lastblock = count;
                ms.Write(buffer, 0, count);
                count = inputStream.Read(buffer, 0, readSize);
                i++;
            }
            log.Debug("BlockCount:" + i);
            ms.Position = 0;
            inputStream.Close();
            return ms;
        }

        private string txt="";
        private string endBoundary="";
        private void alldispo(Stream rm) {
            string line;
            rm.Position = 0;
            txt = "";
            int time = 0;
            int foundbdry = 0;
            StreamReader r = new StreamReader(rm);
            while ((line = r.ReadLine()) != null) {
                if (time > 1) {
                    endBoundary += line;
                }
                if (line.Contains("-------")) {
                    time++;
                    endBoundary += line;
                    log.Debug("eb:" +time+"-"+ line);
                }
                
                if ((line.Contains("Content-Disposition")) && (foundbdry == 0)) {
                    log.Debug("line:" + line);
                    txt += line + "/r/n";    
                }
                if (line.Contains("Content-Type") && (foundbdry == 0)) {
                    log.Debug("line:" + line);
                    txt += line+"/n";
                    foundbdry++;
                    //break;
                }
            }
            rm.Position = 0;
        }
        private void ReadWriteStream(Stream readStream, Stream writeStream, Encoding enc, string boundary) {
            Byte[] boundaryBytes = enc.GetBytes(boundary);
            Int32 newlinelen = enc.GetBytes("\n").Length;
            Int32 spacelen = enc.GetBytes(" \r \n \n").Length;

            int Length = 1024;
            Stream responseStream = CopyAndClose(readStream, Length);
            alldispo(responseStream);
            //responseStream.Position = 0;
            log.Debug("txt:" + txt);
            Int32 dispo = enc.GetBytes(txt+spacelen).Length;

            //                          Content-Disposition: form-data; name="my_file"; filename="tfd.pcap" Content-Type: application/octet-stream
            Int32 boundaryLen = boundaryBytes.Length +newlinelen+ dispo;
            int i = 0;
            log.Debug("boundary:" + boundaryBytes.Length);
            log.Debug("completeBoundary Len:" + boundaryLen);
            log.Debug("encbytes:" + newlinelen);
            log.Debug("dispo:" + dispo);
            log.Debug("length:" + leng);
            log.Debug("endbound:" + endBoundary);
            //startPos = IndexOf(buffer, len, boundaryBytes);
            //boundaryLen = 0;
            //MemoryStream ms = new MemoryStream();
            //readStream.CopyTo(ms);
            //Int32 endPos = IndexOf(buffer, len, boundaryBytes);
            
            //------------
            
            // Do something with the stream
            responseStream.Position = 0;

            //MemoryStream ms = new MemoryStream();
            //int streamlen = 0;
            Byte[] buffer = new Byte[Length];
            int bytesRead = responseStream.Read(buffer, 0, Length);

            //while (bytesRead > 0) {
            //    bytesRead = responseStream.Read(buffer, 0, Length);
            //    streamlen += bytesRead;
                
            //}
            int surplus = 0;
            //int streamlen = StreamLength(Length,ms);
            int j = (int)Math.Ceiling((Convert.ToDouble(leng) / Convert.ToDouble(Length)));
            if ((lastblock - boundaryBytes.Length) < 0 && (j>1)) {
                j--;
                surplus = lastblock - (boundaryBytes.Length+spacelen);
                log.Debug("surplus:" + surplus);
            }
            //surplus = 0;//asdlfkjas;dlfkjasdl;jkfs-----------------------------------------------------------------------------------------------------

            log.Debug("spaceLen:" + spacelen);
            log.Debug("newline:"+newlinelen);
            log.Debug("boundaryBytes:" + boundaryBytes.Length);
            log.Debug("bytesread:" + bytesRead);
            log.Debug("length:" + leng);//- boundaryLen));
            log.Debug("Splitted in:" + j );
            log.Debug("LastBlock:" + lastblock);
            // write the required bytes
            //bytesRead = responseStream.Read(buffer, 0, Length);
            while (i < j) {
                //log.Debug(i + ":Buffer:" + bytesRead);
                if ((i == j - 1) && (i == 0)) {
                    writeStream.Write(buffer, boundaryLen, bytesRead - boundaryBytes.Length - boundaryLen - spacelen );
                    log.Debug("writing:"+i+"-" + boundaryLen + "-" + Convert.ToString(bytesRead - boundaryBytes.Length - boundaryLen - spacelen) +"-" +ASCIIEncoding.ASCII.GetString(buffer));
                } else
                    if (i == j - 1) {
                        writeStream.Write(buffer, 0, bytesRead + surplus - boundary.Length -spacelen);
                        log.Debug("writing:" + i + "-" + 0 + "-" + Convert.ToString(bytesRead + surplus - boundaryBytes.Length - spacelen));
                    } else
                        if (i == 0) {
                            //writeStream.Write(buffer, boundaryLen, Length);
                            writeStream.Write(buffer, boundaryLen, bytesRead - boundaryLen);
                            log.Debug("writing:" + i + "-" + boundaryLen + "-" + Convert.ToString(bytesRead - boundaryLen));
                        } else {
                            writeStream.Write(buffer, 0, bytesRead);
                            log.Debug("writing:" + i + "-" + 0 + "-" + Convert.ToString(bytesRead));
                        }
                i++;
                bytesRead = responseStream.Read(buffer, 0, Length);
                log.Debug("bytesread:" + bytesRead);
            }
            

            responseStream.Close();
            readStream.Close();
            writeStream.Close();
        }

        

        private void backgroundWorker2_DoWork(object sender, DoWorkEventArgs e) {
            
            //while (true) {    
                string filelocation = @"C:\file.pcap";
                try {
                    HttpListener listener = new HttpListener();
                    //listener.Start();
                    listener.Prefixes.Add("http://*:808/");
                    listener.Start();
                    HttpListenerContext context = listener.GetContext();
                    string saveTo = filelocation;
                    FileStream writeStream = new FileStream(saveTo, FileMode.Create, FileAccess.Write);
                    ReadWriteStream(context.Request.InputStream, writeStream, context.Request.ContentEncoding, GetBoundary(context.Request.ContentType));
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "text/html";
                    using (StreamWriter writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8)) {
                        writer.WriteLine("File Uploaded - ");
                        log.Debug("File Uploaded - " + context.Request.ContentEncoding + " - " + GetBoundary(context.Request.ContentType));
                    }
                    context.Response.Close();
                    listener.Close();
                    listener.Stop();
                } catch (Exception ex) {
                    log.Debug("An Exception Occurred while Listening :" + ex.ToString());
                }
                
            //} //while
            //listener.Stop();

        }

        //var lines = File.ReadAllLines(filelocation);
        //File.WriteAllLines(filelocation, lines.Skip(3).ToArray());

        //log.Debug("boundary:"+GetBoundary(context.Request.ContentType));
        //SaveFile(context.Request.ContentEncoding, GetBoundary(context.Request.ContentType), context.Request.InputStream);
        //System.IO.File.WriteAllText(@"C:\file01.txt", txt);

        //Stream st = context.Request.InputStream;
        //FileStream fileStream = File.Create("c:\\file.txt", (int)st.Length);
        //byte[] bytesInStream = new byte[st.Length];
        //st.Read(bytesInStream, 0, bytesInStream.Length);
        //fileStream.Write(bytesInStream, 0, bytesInStream.Length);
        //using (Stream output = File.OpenWrite("c:\\file2.txt"))
        //using (Stream input = context.Request.InputStream)
        //{
        //    byte[] buffer = new byte[8192];
        //    int bytesRead;
        //    while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
        //    {
        //        output.Write(buffer, 0, bytesRead);
        //    }
        //}


        //SaveFile(context.Request.ContentEncoding, GetBoundary(context.Request.ContentType), context.Request.InputStream);
        //context.Response.SendChunked = false;

        private void backgroundWorker3_DoWork(object sender, DoWorkEventArgs e) {
            
            
            
        }

        string filelocation;
        private void backgroundWorker4_DoWork(object sender, DoWorkEventArgs e) {
            
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://*:"+port+@"/");
            listener.Start();
            while (true) {
                try {
                    //listener.Start();
                    //HttpListener listener = (HttpListener)e.Argument;
                    HttpListenerContext context = listener.GetContext();
                    filelocation = textBox1.Text + textBox2.Text;
                    log.Debug("filelocation:" + filelocation);
                    string saveTo = filelocation;
                    FileStream writeStream = new FileStream(saveTo, FileMode.Create, FileAccess.Write);
                    ReadWriteStream(context.Request.InputStream, writeStream, context.Request.ContentEncoding, GetBoundary(context.Request.ContentType));
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "text/html";
                    using (StreamWriter writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8)) {
                        writer.WriteLine("File Uploaded - ");
                        log.Debug("File Uploaded - " + context.Request.ContentEncoding + " - " + GetBoundary(context.Request.ContentType));
                    }
                    context.Response.Close();

                } catch (Exception ex) {
                    log.Error("An Exception Occurred while Listening :" + ex.ToString());
                    break;
                }
            }//while
            //listener.Close();
            listener.Stop();
        }

        private void CaptureChange() {
            string fname = textBox2.Text;
            string options = textBox3.Text;
            string path = "/tmp/";
            textBox4.Text="tcpdump -i any -vvv -w "+path+fname+" "+options;
        }
        private void CurlChange() {
            string fname = textBox2.Text;
            string myIP = textBox7.Text;
            string port = textBox6.Text;
            //string options = textBox3.Text;
            string path = "/tmp/";
            textBox5.Text = "curl -F \"my_file=@"+path+fname+"\" http://"+myIP+":"+port;
        }

        string commonname = "file";
        private void button3_Click(object sender, EventArgs e) {
            DateTime now = DateTime.Now;
            commonname = now.ToString("yyMMddhhmmss");
            textBox2.Text = "file" + commonname + ".pcap";
        }

        private void button1_Click(object sender, EventArgs e) {
            Clipboard.SetText(textBox4.Text+"\n");
        }

        private void textBox2_TextChanged(object sender, EventArgs e) {
            CaptureChange();
            CurlChange();
        }

        private void textBox3_TextChanged(object sender, EventArgs e) {
            CaptureChange();
        }

        private void textBox6_TextChanged(object sender, EventArgs e) {
            CurlChange();
        }

        private void textBox7_TextChanged(object sender, EventArgs e) {
            CurlChange();
        }
        private void CheckIP() {
            IPHostEntry host;
            string localIP = "?";
            host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList) {
                log.Debug(ip.ToString());
                if ((ip.ToString()).Contains("10.90.20")) {
                    localIP = ip.ToString();
                }
            }
            textBox7.Text = localIP;
            //return localIP;
        }

        private void button2_Click(object sender, EventArgs e) {
            Clipboard.SetText(textBox5.Text + "\n");
        }

        private void button4_Click(object sender, EventArgs e) {
            string Path = textBox1.Text;
            Process.Start(Path);
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e) {

        }

        private void listBox1_MouseDoubleClick(object sender, MouseEventArgs e) {
            if (listBox1.SelectedIndex != -1) {
                string fileSelected = listBox1.SelectedItem.ToString();
                Process.Start(fileSelected);
            }
        }

        private void listBox1_MouseClick(object sender, MouseEventArgs e) {
            
        }

        private void listBox1_MouseDown(object sender, MouseEventArgs e) {
            listBox1.SelectedIndex = listBox1.IndexFromPoint(e.X, e.Y);
            if (listBox1.SelectedIndex != -1) {
                if (e.Button == MouseButtons.Right) {
                    string file = listBox1.SelectedItem.ToString();

                    Microsoft.Office.Interop.Outlook.Application app = new Microsoft.Office.Interop.Outlook.Application();
                    Microsoft.Office.Interop.Outlook.MailItem mailItem = app.CreateItem(Microsoft.Office.Interop.Outlook.OlItemType.olMailItem);
                    mailItem.Subject = "PCAP: " + file;
                    mailItem.To = "noc@conexiant.net";
                    mailItem.Body = "This is the requested pcap";
                    mailItem.Attachments.Add(file);//logPath is a string holding path to the log.txt file
                    mailItem.Display(false);


                    //Process.Start("mailto:noc@conexiant.net?subject=pcap&attach=" + file);
                    //log.Debug("mailto:noc@conexiant.net?subject=pcap&attach=" + file);
                }
            }
        }
    }

}
