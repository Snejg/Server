using System.Collections.Generic;
using System.Windows.Forms;
using System.Net.Sockets;
using System;
using System.Net;
using System.IO;

namespace Server
{
    public partial class Server : Form
    {
        private static Socket _serverSocket;
        private static List<Socket> _clientSockets = new List<Socket>();
        private static List<bool> _nextRound = new List<bool>(4);
        private static List<bool> _allPlayersReady = new List<bool>(4);
        private static List<Int32> _materialQue = new List<int>();
        private static List<Int32> _infoOrderQue = new List<int>();

        private static int _PORT;
        private const int _BUFFER_SIZE = 2048;
        private static readonly byte[] _buffer = new byte[_BUFFER_SIZE];

        private static Random _custOrder = new Random();
        private static Dictionary<int, bool> _initRole = new Dictionary<int, bool>();       
        private static bool _dataShifted = false;
        private static bool _disconnectAllClients = false;
        private static bool _endOfGame = false;
        private static readonly string _timeStamp = DateTime.Now.ToLongTimeString(); //DateTime.Now.ToString("h:mm:ss tt");
        private static int _roundNumber = 1;
        private static int _clientCount = 0;
        private static int _finalCosts = 0;
        private static int _averageCosts = 0;

        public Server(int port_number)
        {
            _PORT = port_number;
            _initRole.Add(0, false);
            _initRole.Add(1, false);
            _initRole.Add(2, false);
            _initRole.Add(3, false);
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

            /*
            System.Drawing.Rectangle resolution = Screen.PrimaryScreen.Bounds;            
            textBox_log.Invoke(new MethodInvoker(delegate ()
            { textBox_log.AppendText(resolution.Height.ToString() + " x " + resolution.Width.ToString()); }));
            */
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
            _clientCount++;            
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
                _disconnectAllClients = true;                
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(_buffer, recBuf, received);
            Int32 role = BitConverter.ToInt32(recBuf,0);
            Int32 boxOut = BitConverter.ToInt32(recBuf, 4);
            Int32 reqOut = BitConverter.ToInt32(recBuf, 8);
            Int32 stock = BitConverter.ToInt32(recBuf, 12);
            Int32 u_orders = BitConverter.ToInt32(recBuf, 16);
            Int32 roundCode = BitConverter.ToInt32(recBuf, 20);

            if (_disconnectAllClients || _clientCount > 4)
            {
                Message m = new Message(0, 400, 400, -900); // disconnect all users
                byte[] data = m.getMessageByteArray();
                current.Send(data);
                _clientCount = 4;
                current.Close(); // Dont shutdown because the socket may be disposed and its disconnected anyway
                _clientSockets.Remove(current);
                Console.WriteLine("Velikost bufferu pro sokety: " + _clientSockets.Count.ToString());
            }
            else
            {
                if (roundCode == -600) // load configuration + role
                {                    
                    initClientByRole(current);
                }                               
                else if (reqOut != 0 && boxOut != 0 && roundCode == -500)
                {
                    // chci data - dosly poprve
                    updeteRoundCounter(role);
                    updateDataQues(role, boxOut, reqOut);
                    writeToFile(role, stock, u_orders);

                    this.textBox_log.Invoke(new MethodInvoker(delegate ()
                    { textBox_log.AppendText("Hrac<" + role.ToString() + "> pozaduje: "+ reqOut.ToString() + " posila: " + boxOut.ToString() + "\n"); }));

                    Message m = new Message(role, 300, 300, -300); // waiting
                    byte[] data = m.getMessageByteArray();
                    current.Send(data);

                }
                else if (roundCode == -300) // client ceka az server posle 200 - new round
                {
                    if (isNextRound())  // vsichni uz odeslali sva data - server musi vsem zaslat "new round"
                    {
                        _allPlayersReady[role] = true;

                        if (!_dataShifted)
                        {
                            shiftQueByNewValue();
                            _dataShifted = true;
                            _roundNumber++;
                        }
                        if (arePlayerReady()) // jsou obslouzeni vsichni - odpovi nic nedelej
                        {
                            resetRoundCounter();
                        }
                        sendDataByRole(role, current); // new round
                    }
                    else
                    {
                        Message m = new Message(role, 400, 400, -400); // do nothing
                        byte[] data = m.getMessageByteArray();
                        current.Send(data);
                    }
                }
                else
                {
                    Message m = new Message(role, 400, 400, -400); // do nothing
                    byte[] data = m.getMessageByteArray();
                    current.Send(data);
                }         
            current.BeginReceive(_buffer, 0, _BUFFER_SIZE, SocketFlags.None, ReceiveCallback, current);
            }
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

        private void initClientByRole(Socket current)
        {
            int activeRole = getRole();
            sendDataByRole(activeRole, current);
        }
        
        private int getRole()
        {
            foreach (KeyValuePair<int, bool> pair in _initRole)
            {
                if(pair.Value == false)
                {
                    _initRole[pair.Key] = true;
                    return pair.Key;
                }
            }
            return -1;
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
            if(_roundNumber > 36)
            {
                if (!_endOfGame)
                {
                    _finalCosts = getFinalCosts();
                    _averageCosts = Convert.ToInt32(getAverageCosts());
                    _endOfGame = true;
                    writeToFinalScore(_finalCosts);
                    //System.Diagnostics.Process.Start("client.exe", "100 192.168.1.2");
                }
                
                Message m = new Message(role, _averageCosts, _finalCosts, -1000); // end game occur
                byte[] data = m.getMessageByteArray();
                current.Send(data);
            }
            else
            {
                Message m = new Message(role, boxIn, boxReqIn, -200); // new round
                byte[] data = m.getMessageByteArray();
                current.Send(data);
            }
            
        }

        float getAverageCosts()
        {
            float sum = 0;
            int numOfGames = 0;
            try
            {
                string line;
                // Read the file and display it line by line.
                System.IO.StreamReader file = new System.IO.StreamReader(@"scores.csv");
                while ((line = file.ReadLine()) != null)
                {
                    sum += Int32.Parse(line);
                    numOfGames++;
                }
                file.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("The file could not be read:");
                Console.WriteLine(e.Message);
            }

            if (numOfGames == 0)
            {
                return 0;
            }
            else
            {
                float ret = sum / numOfGames;
                return ret;
            }
        }

        int getFinalCosts()
        {
            int finalCosts = 0;
            char delimiter = ';';
            try
            {
                string line;
                // Read the file and display it line by line.
                System.IO.StreamReader file = new System.IO.StreamReader(@"game.csv");
                while ((line = file.ReadLine()) != null)
                {
                    //System.Console.WriteLine(line);
                    string[] lineValues = line.Split(delimiter);
                    if (lineValues[0] == _timeStamp.ToString())
                    {
                        int costValue = Int32.Parse(lineValues[lineValues.Length - 2]); // 3
                        if (costValue >= 0)
                        {
                            int costs = costValue * 50;
                            finalCosts += costs;
                        }
                        else
                        {
                            int costs = Math.Abs(costValue) * 100;
                            finalCosts += costs;
                        }
                    }
                    //Console.WriteLine(line);
                }
                file.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("The file could not be read:");
                Console.WriteLine(e.Message);
            }
            return finalCosts;
        }

        public void writeToFinalScore(int finalScore)
        {
            string path = @"scores.csv";

            string line = finalScore.ToString();

            if (!File.Exists(path))
            {
                // Create a file to write to.
                //string header = "SKORE;";
                using (StreamWriter sw = File.CreateText(path))
                {
                    sw.WriteLine(line);
                }
            }
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(line);
            }
        }

        private void shiftQueByNewValue()
        {
            _materialQue.Insert(0,_infoOrderQue[7]);
            if(_roundNumber <= 4)
            {
                _infoOrderQue.Insert(0, 4);
            }
            else
            {
                _infoOrderQue.Insert(0, 8);
            }
            //_infoOrderQue.Insert(0,_custOrder.Next(2, 25));
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
            _materialQue.Add(4);
            _materialQue.Add(12);
            _materialQue.Add(4);
            _materialQue.Add(12);
            _materialQue.Add(4);
            _materialQue.Add(12);
            _materialQue.Add(4);
            _materialQue.Add(12);

            _infoOrderQue.Add(4);
            _infoOrderQue.Add(4);
            _infoOrderQue.Add(4);
            _infoOrderQue.Add(4);
            _infoOrderQue.Add(4);
            _infoOrderQue.Add(4);
            _infoOrderQue.Add(4);
            _infoOrderQue.Add(4);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            initRoundCounter();
            initQues();
            SetupServer();
            //writeToFile(0,0,0);
        }

        private void add2Chart(int role, int value)
        {

            switch (role)
            {
                case 0:
                    this.chart1.Invoke(new MethodInvoker(delegate ()
                    { chart1.Series["Továrník"].Points.AddXY(_roundNumber, value); }));                    
                    break;
                case 1:
                    this.chart1.Invoke(new MethodInvoker(delegate ()
                    { chart1.Series["Distributor"].Points.AddXY(_roundNumber, value); }));                    
                    break;
                case 2:
                    this.chart1.Invoke(new MethodInvoker(delegate ()
                    { chart1.Series["Velko-obchodník"].Points.AddXY(_roundNumber, value); }));
                    break;
                case 3:
                    this.chart1.Invoke(new MethodInvoker(delegate ()
                    { chart1.Series["Malo-obchodník"].Points.AddXY(_roundNumber, value); }));
                    break;
                default:
                    break;
            }
        }

        public void writeToFile(int role, int stock, int u_orders)
        {
            string path = @"game.csv";
            int value;
            if (stock == 0)
            {
                value = - u_orders;
            }
            else
            {
                value = stock;
            }
            
            string barrels = _timeStamp + ";" + _roundNumber + ";" + role + ";" + value + ";";
            add2Chart(role, value);
                             
            if (!File.Exists(path))
            {
                // Create a file to write to.
                string header = "ID;KOLO;ID_HRAC;HODNOTA;";
                using (StreamWriter sw = File.CreateText(path))
                {
                    sw.WriteLine(header);
                }
            }
            using (StreamWriter sw = File.AppendText(path))
            {                
                //sw.WriteLine(orders);
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

        void deleteFileByName(string name)
        {
            if ((File.Exists(name)))
            {
                File.Delete(name);
                textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("Soubor " + name + " byl smazan \n"); }));
            }
            else
            {
                textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("Soubor " + name + " neexistuje \n"); }));
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //int newPort = _PORT + 1;
            //System.Diagnostics.Process.Start(@"C:\Users\s1398\Documents\Diplomka\Server\Server\Server\bin\Debug\Server.exe", newPort.ToString());
            deleteFileByName("game.csv");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            deleteFileByName("scores.csv");
        }
    }
}
