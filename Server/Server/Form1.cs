using System.Collections.Generic;
using System.Windows.Forms;
using System.Net.Sockets;
using System;
using System.Text;
using System.Net;

namespace Server
{
    public partial class Form1 : Form
    {
        private static Socket _serverSocket;
        private static readonly List<Socket> _clientSockets = new List<Socket>();
        //private static Dictionary<int, Socket> _gemeSockets = new Dictionary<int, Socket>();

        private static int _PORT;

        private const int _BUFFER_SIZE = 2048;
        private static readonly byte[] _buffer = new byte[_BUFFER_SIZE];
        private static List<bool> _nextRound = new List<bool>(2);

        public Form1(int port_number)
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
            Int32 reqOut = BitConverter.ToInt32(recBuf, 4);
            Int32 boxOut = BitConverter.ToInt32(recBuf, 8);

            if(_nextRound[role] == true) // uz jednou data mam - cekas na broadcast
            {
                if (isNextRound())
                {
                    byte[] data = Encoding.ASCII.GetBytes("new");
                    current.Send(data);
                    resetRoundCounter();
                }
                else
                {
                    byte[] data = Encoding.ASCII.GetBytes("nothing to do");
                    current.Send(data);
                }
            }
            else  // chci data - dosly poprve
            {            
                updeteRoundCounter(role);

                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("Role: " + role.ToString() + "\n"); }));

                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("reqOut: " + reqOut.ToString() + "\n"); }));

                this.textBox_log.Invoke(new MethodInvoker(delegate ()
                { textBox_log.AppendText("boxOut: " + boxOut.ToString() + "\n"); }));

                if (isNextRound())
                {
                    //next round - all players have finished
                    byte[] data = Encoding.ASCII.GetBytes("new");
                    current.Send(data);
                    doNextRoundBroadcast(); 
                }
                else
                {
                    // still waiting for all players
                    byte[] data = Encoding.ASCII.GetBytes("waiting");
                    current.Send(data);
                }
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

        private void resetRoundCounter()
        {
            for(int i = 0; i < _nextRound.Count; i++)
            {
                _nextRound[i] = false;
            }
        }

        private void doNextRoundBroadcast()
        {

        }

        private void updeteRoundCounter(int atIndex)
        {
            _nextRound[atIndex] = true;
        }

        private bool isNextRound()
        {
            for (int i = 0; i < _nextRound.Count; i++)
            {
                if(_nextRound[i] == false)
                {
                    return false;
                }                
            }
            return true;
        }
    
        private void Form1_Load(object sender, EventArgs e)
        {
            _nextRound.Add(false);
            _nextRound.Add(false);
            resetRoundCounter();
            SetupServer();
        }



    }
}
