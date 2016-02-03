using System.Collections.Generic;
using System.Windows.Forms;
using System.Net.Sockets;
using System;
using System.Text;
using System.Net;
using System.IO;

namespace Server
{
    public partial class Server : Form
    {
        private static Socket _serverSocket;
        private static readonly List<Socket> _clientSockets = new List<Socket>();
  
        private static int _PORT;
        private const int _BUFFER_SIZE = 2048;
        private static readonly byte[] _buffer = new byte[_BUFFER_SIZE];
        private static List<bool> _nextRound = new List<bool>(4);
        private static List<bool> _allPlayersReady = new List<bool>(4);
        private static Random _custOrder = new Random();

        private static List<Int32> _materialQue = new List<int>();
        private static List<Int32> _infoOrderQue = new List<int>();
        private static bool _dataShifted = false;
        private static readonly string _timeStamp = DateTime.Now.ToLongTimeString(); //DateTime.Now.ToString("h:mm:ss tt");
        private static int _roundNumber = 1;

        public Server(int port_number)
        {
            _PORT = port_number;
            InitializeComponent();
        }

        public void SetupServer()
        {
            textBox_log.Invoke(new MethodInvoker(delegate ()
            { textBox_log.AppendText("Setting up server... \n"); }));

            textBox_log.Invoke(new MethodInvoker(delegate ()
            { textBox_log.AppendText("Listening on port: " + _PORT + " \n"); }));

            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverSocket.Bind(new IPEndPoint(IPAddress.Any, _PORT));
            _serverSocket.Listen(5);
            _serverSocket.BeginAccept(AcceptCallback, null);

            textBox_log.Invoke(new MethodInvoker(delegate ()
            { textBox_log.AppendText("Server setup complete \n"); }));

        }

        private void AcceptCallback(IAsyncResult AR)
        {
            Socket socket;

            try
            {
                socket = _serverSocket.EndAccept(AR);
            }
            catch (ObjectDisposedException) // I cannot seem to avoid this (on exit when properly closing sockets)
            {
                return;
            }

            _clientSockets.Add(socket);
            //int size = _gemeSockets.Count;
            //_gemeSockets.Add(size, socket);
            socket.BeginReceive(_buffer, 0, _BUFFER_SIZE, SocketFlags.None, ReceiveCallback, socket);

            this.textBox_log.Invoke(new MethodInvoker(delegate ()
            { textBox_log.AppendText("Client connected, waiting for request...\n"); }));

            _serverSocket.BeginAccept(AcceptCallback, null);
        }

        private void ReceiveCallback(IAsyncResult AR)
        {
            Socket current = (Socket)AR.AsyncState;
            int received;

            try
            {
                received = current.EndReceive(AR);
            }
            catch (SocketException x)
            {
                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("Client forcefully disconnected \n"); }));

                current.Close(); // Dont shutdown because the socket may be disposed and its disconnected anyway
                _clientSockets.Remove(current);
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(_buffer, recBuf, received);
            //string text = Encoding.ASCII.GetString(recBuf);
            Int32 role = BitConverter.ToInt32(recBuf,0);
            Int32 boxOut = BitConverter.ToInt32(recBuf, 4);
            Int32 reqOut = BitConverter.ToInt32(recBuf, 8);
            Int32 roundCode = BitConverter.ToInt32(recBuf, 12);

            Int32 boxIn;    // output value
            Int32 boxReqIn; // output value       

            if (roundCode == 600) // load configuration
            {
                sendDataByRole(role, current);
                /*
                switch (role)
                {
                    case 0:
                        boxIn = _materialQue[1];
                        boxReqIn = _infoOrderQue[6];
                        break;
                    case 1:
                        boxIn = _materialQue[3];
                        boxReqIn = _infoOrderQue[4];
                        break;
                    case 2:
                        boxIn = _materialQue[5];
                        boxReqIn = _infoOrderQue[2];
                        break;
                    case 3:
                        boxIn = _materialQue[7];
                        boxReqIn = _infoOrderQue[0];
                        break;
                    default:
                        boxIn = 0;
                        boxReqIn = 0;
                        break;
                }

                Message m = new Message(role, boxIn, boxReqIn, 200); // new round
                byte[] data = m.getMessageByteArray();
                current.Send(data);
                */

            }
            else if (reqOut != 0 && boxOut != 0 && roundCode == 500)
            {
                // chci data - dosly poprve
                updeteRoundCounter(role);
                updateDataQues(role, boxOut, reqOut);

                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("Role: " + role.ToString() + "\n"); }));

                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("reqOut: " + reqOut.ToString() + "\n"); }));

                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("boxOut: " + boxOut.ToString() + "\n"); }));

                Message m = new Message(role, 300, 300, 300); // waiting
                byte[] data = m.getMessageByteArray();
                current.Send(data);

            }
            else if (roundCode == 300) // client ceka az server posle 200 - new round
            {
                if (isNextRound())  // vsichni uz odeslaly sva data - server musi vsem zaslat "new round"
                {
                    _allPlayersReady[role] = true;

                    if (!_dataShifted)
                    {
                        writeToFile();
                        shiftQueByNewValue();
                        _dataShifted = true;
                    }
                    if (arePlayerReady()) // jsou obslouzeni vsichni - odpovi nic nedelej
                    {
                        resetRoundCounter();
                        //writeToFile();
                        //shiftQueByNewValue();
                    }

                    sendDataByRole(role, current);
                    /*
                    Message m = new Message(role, boxIn, boxReqIn, 200); // new round
                    byte[] data = m.getMessageByteArray();
                    current.Send(data);
                    */
                }
                else
                {
                    Message m = new Message(role, 400, 400, 400); // do nothing
                    byte[] data = m.getMessageByteArray();
                    current.Send(data);
                }
            }
            else
            {
                Message m = new Message(role, 400, 400, 400); // do nothing
                byte[] data = m.getMessageByteArray();
                current.Send(data);
            }

            /*
            this.textBox_log.Invoke(new MethodInvoker(delegate ()
            { textBox_log.AppendText("Received Text: " + text + "\n"); }));

            
            if (text.ToLower() == "next round") // Client requested time
            {
                byte[] data = Encoding.ASCII.GetBytes(DateTime.Now.ToLongTimeString());
                current.Send(data);
            }
            else if (text.ToLower() == "exit") // Client wants to exit gracefully
            {
                // Always Shutdown before closing
                current.Shutdown(SocketShutdown.Both);
                current.Close();
                _clientSockets.Remove(current);

                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("Client disconnected \n"); }));

                return;
            }
            else
            {
                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("Text is an invalid request \n"); }));

                byte[] data = Encoding.ASCII.GetBytes("Invalid request");
                current.Send(data);
            }
            */

            current.BeginReceive(_buffer, 0, _BUFFER_SIZE, SocketFlags.None, ReceiveCallback, current);
        }

        private void updateDataQues(Int32 role, Int32 boxOut, Int32 reqOut)
        {
            switch (role)
            {
                case 0:
                    _materialQue[1] = boxOut;
                    _infoOrderQue[6] = reqOut;
                    break;
                case 1:
                    _materialQue[3] = boxOut;
                    _infoOrderQue[4] = reqOut;
                    break;
                case 2:
                    _materialQue[5] = boxOut;
                    _infoOrderQue[2] = reqOut;
                    break;
                case 3:
                    _materialQue[7] = boxOut;
                    _infoOrderQue[0] = reqOut;
                    break;
                default:
                    break;
            }
        }

        private void sendDataByRole(Int32 role, Socket current)
        {
            Int32 boxIn;
            Int32 boxReqIn;
            switch (role)
            {
                case 0:
                    boxIn = _materialQue[1];
                    boxReqIn = _infoOrderQue[6];
                    break;
                case 1:
                    boxIn = _materialQue[3];
                    boxReqIn = _infoOrderQue[4];
                    break;
                case 2:
                    boxIn = _materialQue[5];
                    boxReqIn = _infoOrderQue[2];
                    break;
                case 3:
                    boxIn = _materialQue[7];
                    boxReqIn = _infoOrderQue[0];
                    break;
                default:
                    boxIn = 0;
                    boxReqIn = 0;
                    break;
            }

            Message m = new Message(role, boxIn, boxReqIn, 200); // new round
            byte[] data = m.getMessageByteArray();
            current.Send(data);
        }

        private void shiftQueByNewValue()
        {
            _materialQue.Insert(0,_infoOrderQue[7]);
            _infoOrderQue.Insert(0,_custOrder.Next(2, 25));
        }

        private void resetRoundCounter()
        {
            for(int i = 0; i < _nextRound.Count; i++)
            {
                _nextRound[i] = false;
            }

            for (int i = 0; i < _allPlayersReady.Count; i++)
            {
                _allPlayersReady[i] = false;
            }

            _dataShifted = false;
            _roundNumber++;
        }

        private void updeteRoundCounter(int atIndex)
        {
            _nextRound[atIndex] = true;
        }

        private bool isNextRound()
        {
            for (int i = 0; i < _nextRound.Count; i++)
            {
                if (_nextRound[i] == false)
                {
                    return false;
                }                
            }
            return true;
        }

        private bool arePlayerReady()
        {
            for (int i = 0; i < _allPlayersReady.Count; i++)
            {
                if (_allPlayersReady[i] == false)
                {
                    return false;
                }
            }
            return true;
        }

        private void initRoundCounter()
        {
            _nextRound.Add(false);
            _nextRound.Add(false);
            _nextRound.Add(false);
            _nextRound.Add(false);

            _allPlayersReady.Add(false);
            _allPlayersReady.Add(false);
            _allPlayersReady.Add(false);
            _allPlayersReady.Add(false);
        }

        private void initQues() // startup configuration
        {
            _materialQue.Add(1);
            _materialQue.Add(2);
            _materialQue.Add(3);
            _materialQue.Add(4);
            _materialQue.Add(5);
            _materialQue.Add(6);
            _materialQue.Add(7);
            _materialQue.Add(8);

            _infoOrderQue.Add(1);
            _infoOrderQue.Add(2);
            _infoOrderQue.Add(3);
            _infoOrderQue.Add(4);
            _infoOrderQue.Add(5);
            _infoOrderQue.Add(6);
            _infoOrderQue.Add(7);
            _infoOrderQue.Add(8);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            initRoundCounter();
            initQues();
            SetupServer();
            writeToFile();
        }

        public void writeToFile()
        {
            string path = @"game.csv";

            string barrels = _timeStamp + ";" + _roundNumber + ";";
            barrels = barrels + _materialQue[1].ToString() + ";";
            barrels = barrels + _materialQue[3].ToString() + ";";
            barrels = barrels + _materialQue[5].ToString() + ";";
            barrels = barrels + _materialQue[7].ToString() + ";";

            string orders = _timeStamp + ";" + _roundNumber + ";";
            orders = orders + _infoOrderQue[6].ToString() + ";";
            orders = orders + _infoOrderQue[4].ToString() + ";";
            orders = orders + _infoOrderQue[2].ToString() + ";";
            orders = orders + _infoOrderQue[0].ToString() + ";";
                                
            if (!File.Exists(path))
            {
                // Create a file to write to.
                string header = "ID;KOLO;HRAC_1;HRAC_2;HRAC_3;HRAC_4;";
                using (StreamWriter sw = File.CreateText(path))
                {
                    sw.WriteLine(header);
                }
            }
            using (StreamWriter sw = File.AppendText(path))
            {                
                sw.WriteLine(orders);
                sw.WriteLine(barrels);
            }
        }

        public struct Message
        {
            Int32 role;
            Int32 boxOut;
            Int32 boxReqOut;
            Int32 roundCode;

            public Message(Int32 p_role, Int32 p_boxOut, Int32 p_boxReqOut, Int32 p_roudCode)
            {
                role = p_role;
                boxOut = p_boxOut;
                boxReqOut = p_boxReqOut;
                roundCode = p_roudCode;
            }

            public byte[] getMessageByteArray()
            {
                byte[] data1 = BitConverter.GetBytes(role);
                byte[] data2 = BitConverter.GetBytes(boxOut);
                byte[] data3 = BitConverter.GetBytes(boxReqOut);
                byte[] data4 = BitConverter.GetBytes(roundCode);

                byte[] data = new byte[data1.Length + data2.Length + data3.Length + data3.Length];
                Buffer.BlockCopy(data1, 0, data, 0, data1.Length);
                Buffer.BlockCopy(data2, 0, data, data1.Length, data2.Length);
                Buffer.BlockCopy(data3, 0, data, data1.Length + data2.Length, data3.Length);
                Buffer.BlockCopy(data4, 0, data, data1.Length + data2.Length + data3.Length, data4.Length);
                return data;
            }
        }


    }
}
