using System;
using System.IO;
using System.Net.Sockets;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Hrd2Udp
{
    public partial class Form1 : Form
    {
        private TcpClient tcpClient;
        private UdpClient udpClient;
        private NetworkStream stream;
        private string udpAddr;
        private int udpPort;

        private uint prevFreq = 0;

        public Form1()
        {
            InitializeComponent();
            InitializeUI();
            LoadSettings();
        }

        private void InitializeUI()
        {
            txtIpAddress.Text = "127.0.0.1";
            txtPort.Text = "7809";
            txtRF2KAddress.Text = "192.168.68.67";
            txtRF2KPort.Text = "12060";
            btnStart.Enabled = true;
            btnStop.Enabled = false;
        }

        private void LoadSettings()
        {
            txtIpAddress.Text = Properties.Settings.Default.HRDAddress;
            txtPort.Text = Properties.Settings.Default.HRDPort.ToString();
            txtRF2KAddress.Text = Properties.Settings.Default.RF2KAddress;
            txtRF2KPort.Text = Properties.Settings.Default.RF2KPort.ToString();
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.HRDAddress = txtIpAddress.Text.Trim();
            Properties.Settings.Default.HRDPort = int.Parse(txtPort.Text.Trim());
            Properties.Settings.Default.RF2KAddress = txtRF2KAddress.Text.Trim();
            Properties.Settings.Default.RF2KPort = int.Parse(txtRF2KPort.Text.Trim());
            Properties.Settings.Default.Save();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            try
            {
                string ipAddress = txtIpAddress.Text;
                int port = int.Parse(txtPort.Text);

                tcpClient = new TcpClient();
                tcpClient.Connect(ipAddress, port);
                stream = tcpClient.GetStream();

                udpClient = new UdpClient();
                udpAddr = txtRF2KAddress.Text.Trim();
                udpPort = int.Parse(txtRF2KPort.Text.Trim());

                txtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] Connected to {ipAddress}:{port}\n\n");

                btnStart.Enabled = false;
                btnStop.Enabled = true;
                txtIpAddress.Enabled = false;
                txtPort.Enabled = false;
                txtRF2KAddress.Enabled = false;
                txtRF2KPort.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        //private void btnSend_Click(object sender, EventArgs e)
        //{
        //    try
        //    {
        //        if (stream != null && client.Connected)
        //        {
        //            // Create and send the message
        //            string msg = txtMessage.Text.Trim().Replace(';', '\x09');
        //            byte[] messageBytes = CreateMessage(msg);
        //            stream.Write(messageBytes, 0, messageBytes.Length);
        //            stream.Flush();

        //            txtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] Sent: {txtMessage.Text}\n");

        //            // Receive the response
        //            byte[] response = ReceiveMessage();
        //            if (response != null)
        //            {
        //                string responseText = ParseMessage(response);
        //                //txtResponse.AppendText($"[{DateTime.Now:HH:mm:ss}] Received: {responseText}\n\n");
        //                txtStatus.AppendText($"{responseText}\n\n");
        //            }
        //        }
        //        else
        //        {
        //            MessageBox.Show("Not connected to server", "Warning",
        //                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show($"Communication error: {ex.Message}", "Error",
        //            MessageBoxButtons.OK, MessageBoxIcon.Error);
        //    }
        //}

        private byte[] CreateMessage(string text)
        {
            // Encode the text in UTF-16 with null terminator
            string textWithNull = text + "\0";
            byte[] szText = Encoding.Unicode.GetBytes(textWithNull);

            // Calculate total message size
            int nSize = szText.Length+16;
            uint nSanity1 = 0x1234ABCD;
            uint nSanity2 = 0xABCD1234;
            uint nChecksum = 0;

            // Create the message buffer
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(nSize);        // 4 bytes
                writer.Write(nSanity1);     // 4 bytes
                writer.Write(nSanity2);     // 4 bytes
                writer.Write(nChecksum);    // 4 bytes
                writer.Write(szText);       // variable length

                return ms.ToArray();
            }
        }

        private byte[] ReceiveMessage()
        {
            try
            {
                //byte[] message = new byte[200];
                //int bytesRead = 0;
                //int read = stream.Read(message, bytesRead, 16);
                // Read the header (16 bytes)
                byte[] header = new byte[16];
                int bytesRead = 0;
                while (bytesRead < 16)
                {
                    int read = stream.Read(header, bytesRead, 16 - bytesRead);
                    if (read == 0)
                        throw new Exception("Connection closed by server");
                    bytesRead += read;
                }

                // Parse header
                int nSize = BitConverter.ToInt32(header, 0);
                uint nSanity1 = BitConverter.ToUInt32(header, 4);
                uint nSanity2 = BitConverter.ToUInt32(header, 8);
                uint nChecksum = BitConverter.ToUInt32(header, 12);

                // Validate magic numbers
                if (nSanity1 != 0x1234ABCD || nSanity2 != 0xABCD1234)
                {
                    throw new Exception("Invalid message format - magic numbers don't match");
                }

                // Read the message text
                int txtSize = nSize - 16;
                byte[] szText = new byte[txtSize];
                bytesRead = 0;
                while (bytesRead < txtSize)
                {
                    int read = stream.Read(szText, bytesRead, txtSize - bytesRead);
                    if (read == 0)
                        throw new Exception("Connection closed while reading message");
                    bytesRead += read;
                }

                return szText;
            }
            catch (Exception ex)
            {
                txtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] Receive error: {ex.Message}\n\n");
                return null;
            }
        }

        // returns the current TX frequency
        private uint GetTXFrequency(byte[] szText)
        {
            // Decode UTF-16 and remove null terminator
            string text = Encoding.Unicode.GetString(szText);
            string[] substrs = text.Split('$');
            uint freq0 = uint.Parse(substrs[0]);
            uint freq1 = uint.Parse(substrs[1]);
            uint sel = uint.Parse(substrs[2]);

            return sel == 0 ? freq0 : freq1;
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            try
            {
                if (stream != null)
                {
                    stream.Close();
                    stream = null;
                }
                if (tcpClient != null)
                {
                    tcpClient.Close();
                    tcpClient = null;
                }
                if (udpClient != null)
                {
                    udpClient.Close();
                    udpClient = null;
                }

                txtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] Disconnected\n\n");

                btnStart.Enabled = true;
                btnStop.Enabled = false;
                txtIpAddress.Enabled = true;
                txtPort.Enabled = true;
                txtRF2KAddress.Enabled = true;
                txtRF2KPort.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Disconnect error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Clean up on form close
            if (stream != null)
                stream.Close();
            if (tcpClient != null)
                tcpClient.Close();
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            txtStatus.Clear();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (stream != null && tcpClient.Connected)
            {
                // Create and send the message
                byte[] messageBytes = CreateMessage("get freqx");
                stream.Write(messageBytes, 0, messageBytes.Length);
                stream.Flush();

                //txtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] Sent: {txtMessage.Text}\n");

                // Receive the response
                byte[] response = ReceiveMessage();
                if (response != null)
                {
                    uint txFrequency = GetTXFrequency(response);
                    if (prevFreq != txFrequency)
                    {
                        prevFreq = txFrequency;
                        try
                        {
                            // Create XML document
                            var xml = new XElement("RadioInfo",
                                new XElement("RadioNr", 1),
                                new XElement("TXFreq", txFrequency / 10),
                                new XElement("FocusRadioNr", 1)
                            );

                            XDocument doc = new XDocument(
                                new XDeclaration("1.0", "utf-8", "yes"), // XML version, encoding and standalone attribute
                                xml);

                            StringBuilder sb = new StringBuilder();
                            using (System.IO.TextWriter tr = new System.IO.StringWriter(sb))
                            {
                                doc.Save(tr);
                            }

                            var data = Encoding.UTF8.GetBytes(sb.ToString());

                            // Send UDP packet
                            udpClient.Send(data, data.Length, udpAddr, udpPort);
                            //Console.WriteLine($"Sent XML to {_udpHost}:{_udpPort}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending UDP packet: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            System.GC.Collect();
        }

        private void Form1_FormClosing_1(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
        }
    }
}
