﻿using System;
using System.Collections.Generic;
using System.Net.Sockets;
using EI.SI;
using System.Threading;
using System.IO;
using Room;
using Security;

namespace ClienteHandler
{
    public class ClientHandler
    {
        private static Mutex connection = new Mutex(); //Mutex utilizado para fazer broadcast para os clientes
        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private ProtocolSI protocolSI;
        private room room;
        private static List<room> rooms = new List<room>();
        private static string jogadores = "";
        private security security;
        private int clientID;
        private string nomeJogador;
        private byte[] simetricKey;
        private byte[] IV;

        public ClientHandler(TcpClient client, int clientID)
        {
            tcpClient = client;
            this.clientID = clientID;
            security = new security();
        }

        public void Handle()
        {
            Thread thread = new Thread(threadHandler);
            thread.Start();
        }

        public byte[] getSimetricKey()
        {
            return simetricKey;
        }

        public void enviaACK() //Envia o ACK para o cliente
        {
            byte[] ack = protocolSI.Make(ProtocolSICmdType.ACK);
            networkStream.Write(ack, 0, ack.Length);
        }

        public bool esperaACK() //Espera o ACK do cliente
        {
            networkStream.ReadTimeout = 100;
            try
            {
                networkStream.Read(protocolSI.Buffer, 0, protocolSI.Buffer.Length);
                while (protocolSI.GetCmdType() != ProtocolSICmdType.ACK)
                {
                    networkStream.Read(protocolSI.Buffer, 0, protocolSI.Buffer.Length);
                }
                networkStream.ReadTimeout = -1;
                return true;
            }
            catch (IOException)
            {
                networkStream.ReadTimeout = -1;
                return false;
            }

        }

        public void broadcast(string msg, ProtocolSICmdType cmd, string except = "") //Faz um broadcast para todos os jogadores
        {
            connection.WaitOne(); //Adquire controle do networkStream
            byte[] msgToSend;
            NetworkStream networkStream = tcpClient.GetStream();
            foreach (ClientHandler client in room.getClientList())
            {
                if (!client.nomeJogador.Equals(except))
                {
                    NetworkStream newNetworkStream = client.tcpClient.GetStream(); //Cria uma nova via de comunicação para o client

                    byte[] ack = protocolSI.Make(ProtocolSICmdType.ACK);
                    newNetworkStream.Write(ack, 0, ack.Length);

                    msgToSend = protocolSI.Make(cmd, client.security.CifrarTexto(msg));
                    newNetworkStream.Write(msgToSend, 0, msgToSend.Length);
                    esperaACK();
                }
            }
            connection.ReleaseMutex(); //Libera o networkStream
        }

        private void trocaDePosicao(bool vencedor = false)
        {
            if (vencedor)
            {
                broadcast("", ProtocolSICmdType.USER_OPTION_7);
                esperaACK();
                room.trocaJogadores();
            }
            else if (room.getClientList().Count == 1)
            {
                room.trocaJogadores();
                string msg = String.Format("1/Agora você é o jogador {0}", room.getPosJogador(nomeJogador));
                byte[] msgByte = protocolSI.Make(ProtocolSICmdType.USER_OPTION_7, security.CifrarTexto(msg));
                networkStream.Write(msgByte, 0, msgByte.Length);
                esperaACK();
            } else
            {
                string outroJogador = room.getNomeJogador(room.getPosJogador(nomeJogador) == 1 ? 2 : 1);
                foreach (ClientHandler client in this.room.getClientList())
                {
                    if (client.nomeJogador.Equals(outroJogador))
                    {
                        string msg = String.Format("0/O jogador {0} solicitou trocar de posição, você aceita?", nomeJogador);
                        byte[] msgByte = protocolSI.Make(ProtocolSICmdType.USER_OPTION_7, security.CifrarTexto(msg));

                        connection.WaitOne(); //Adquire controle único do networkStream para fazer o broadcast
                        NetworkStream newNetworkStream = client.tcpClient.GetStream(); //Cria uma nova via de comunicação para aquele client

                        byte[] ack = protocolSI.Make(ProtocolSICmdType.ACK);
                        newNetworkStream.Write(ack, 0, ack.Length);
                        Thread.Sleep(100);
                        newNetworkStream.Write(msgByte, 0, msgByte.Length);
                        newNetworkStream.Read(protocolSI.Buffer, 0, protocolSI.Buffer.Length);

                        ProtocolSICmdType protocolSICmdType = protocolSI.GetCmdType();
                        while (protocolSICmdType != ProtocolSICmdType.USER_OPTION_1 && protocolSICmdType != ProtocolSICmdType.USER_OPTION_2)
                        {
                            newNetworkStream.Read(protocolSI.Buffer, 0, protocolSI.Buffer.Length);
                            protocolSICmdType = protocolSI.GetCmdType();
                        }
                        connection.ReleaseMutex(); //Libera o networkStream
                        if (protocolSICmdType == ProtocolSICmdType.USER_OPTION_1)
                        {
                            msg = "1/Solicitação aceita";
                            msgByte = protocolSI.Make(ProtocolSICmdType.USER_OPTION_7, security.CifrarTexto(msg));
                            networkStream.Write(msgByte, 0, msgByte.Length);
                            broadcast("", ProtocolSICmdType.USER_OPTION_7, nomeJogador);
                            esperaACK();
                            room.trocaJogadores();
                            return;
                        }
                        else
                        {
                            msg = "2/Solicitação negada";
                            msgByte = protocolSI.Make(ProtocolSICmdType.USER_OPTION_7, security.CifrarTexto(msg));
                            networkStream.Write(msgByte, 0, msgByte.Length);
                            esperaACK();
                            return;
                        }
                    }
                }
            }
        }

        private void threadHandler() //Trata as mensagens que chegam e que são enviadas
        {
            networkStream = this.tcpClient.GetStream();
            protocolSI = new ProtocolSI();
            Boolean trocaPosicao = false;
            while (protocolSI.GetCmdType() != ProtocolSICmdType.EOT) //Enquanto a thread não receber ordens para terminar
            {
                networkStream.Read(protocolSI.Buffer, 0, protocolSI.Buffer.Length);
                ProtocolSICmdType cmd = protocolSI.GetCmdType();
                string msg;
                byte[] msgByte;
                switch (protocolSI.GetCmdType())
                {
                    case ProtocolSICmdType.PUBLIC_KEY:
                        security.setPublicKey(protocolSI.GetStringFromData());
                        Console.WriteLine("Recebi uma chave pública");
                        enviaACK();

                        simetricKey = protocolSI.Make(ProtocolSICmdType.SYM_CIPHER_DATA, security.getSimetricKey());
                        networkStream.Write(simetricKey, 0, simetricKey.Length);
                        esperaACK();

                        IV = protocolSI.Make(ProtocolSICmdType.IV, security.getIV());
                        networkStream.Write(IV, 0, IV.Length);
                        esperaACK();
                        break;

                    case ProtocolSICmdType.USER_OPTION_1: //Adquire o nome do jogador
                        connection.WaitOne();       //Caso no qual é feito um broadcast e a thread "errada" recebe o ACK e, portanto
                        connection.ReleaseMutex();  //espera até que a thread "correta" receba o ACK para poder voltar a esperar nova mensagem
                        nomeJogador = security.DecifrarTexto(protocolSI.GetStringFromData());
                        Console.WriteLine("Jogador {0} - {1}, conectou-se", clientID, nomeJogador);
                        enviaACK();
                        break;

                    case ProtocolSICmdType.USER_OPTION_2: //Atualiza os jogadores presentes na sala
                        string salaDesejada = security.DecifrarTexto(protocolSI.GetStringFromData());
                        byte[] newJogador;

                        foreach (room sala in rooms) //Percorre a lista de salas verificando se a sala na qual o cliente deseja conectar-se já existe
                        {
                            if (sala.getName() == salaDesejada)
                            {
                                if (!sala.isFull()) //Verifica se a sala não está cheia
                                {
                                    sala.addClientToRoom(this);
                                    this.room = sala;
                                    break;
                                }
                                else
                                {
                                    goto SalaCheia;
                                }
                            }
                        }

                        if (room == null) //Cria a sala caso a mesma não exista
                        {
                            room = new room(salaDesejada);
                            rooms.Add(room);
                            room.addClientToRoom(this);
                            msg = System.DateTime.Now.ToString();
                            room.writeLog(msg);
                        }

                        Console.WriteLine("{0} entrou na sala {1}", nomeJogador, salaDesejada);
                        enviaACK();

                        if (room.getClientList().Count == 1) //Se aquele jogador é o único na sala
                        {
                            //Coloca o jogador como o jogador 1
                            room.setJogador(nomeJogador);
                            msg = String.Format("1/{0}/", nomeJogador);
                            newJogador = protocolSI.Make(ProtocolSICmdType.USER_OPTION_1, security.CifrarTexto(msg));
                            networkStream.Write(newJogador, 0, newJogador.Length);
                            esperaACK();
                        }
                        else if (room.getClientList().Count ==  2) //Se é o 2º jogador a entrar na sala
                        {
                            int posNovoJogador;
                            room.setJogador(nomeJogador);
                            foreach (ClientHandler client in room.getClientList())
                            {
                                if (client.clientID != clientID)
                                {
                                    posNovoJogador = room.getNomeJogador(2) == nomeJogador ? 2 : 1; //Descobre qual será a posição do novo jogador
                                    msg = String.Format("{0}/{1}/{2}", posNovoJogador, nomeJogador, jogadores);
                                    newJogador = protocolSI.Make(ProtocolSICmdType.USER_OPTION_1, client.security.CifrarTexto(msg));

                                    broadcast(msg, ProtocolSICmdType.USER_OPTION_1, nomeJogador);

                                    //Coloca-se na posição que resta
                                    networkStream.Write(newJogador, 0, newJogador.Length);
                                    esperaACK();
                                }
                                else //Envia o nome do jogador que já está na sala para o novo jogador
                                {
                                    int posJogadorPresente = room.getNomeJogador(1) != nomeJogador ? 1 : 2;
                                    msg = String.Format("{0}/{1}/{2}", posJogadorPresente, room.getNomeJogador(posJogadorPresente), true == room.isMultiplePlayers() ? "true" : "false");
                                    msgByte = protocolSI.Make(ProtocolSICmdType.USER_OPTION_1, security.CifrarTexto(msg));
                                    networkStream.Write(msgByte, 0, msgByte.Length);
                                    esperaACK();
                                }
                            }
                            //Broadcast que informa que há 2 jogadores na sala e, portanto o jogo pode iniciar
                            broadcast(" ", ProtocolSICmdType.USER_OPTION_3);
                        }
                        else //Se a sala já tem 2 jogadores
                        {
                            //Coloca os próximos jogadores na fila
                            room.setJogador(nomeJogador);
                            msg = String.Format("3/{0}/{1}/{2}/{3}/{4}/{5}/{6}", room.getNomeJogador(1), room.getNomeJogador(2), room.getPontos(room.getNomeJogador(1)), 
                                room.getPontos(room.getNomeJogador(2)), room.getPontos("empates"), jogadores, room.getProximoJogadores());
                            newJogador = protocolSI.Make(ProtocolSICmdType.USER_OPTION_1, security.CifrarTexto(msg));
                            networkStream.Write(newJogador, 0, newJogador.Length);
                            msg = String.Format("4/{0}/{1}", jogadores, room.getProximoJogadores());
                            broadcast(msg, ProtocolSICmdType.USER_OPTION_1);
                            esperaACK();
                        }
                        break;
                    SalaCheia:
                        msgByte = protocolSI.Make(ProtocolSICmdType.USER_OPTION_3);
                        networkStream.Write(msgByte, 0, msgByte.Length);
                        esperaACK();
                        break;

                    case ProtocolSICmdType.DATA: //Transmite o que o jogador disse para o chat
                        msg = $"{System.DateTime.Now.ToString("HH:mm:ss")} - {nomeJogador} : {security.DecifrarTexto(protocolSI.GetStringFromData())}";
                        Console.WriteLine(msg);
                        broadcast(msg, ProtocolSICmdType.DATA); //Broadcast da mensagem para todos os jogadores
                        room.writeLog(msg); //Escreve para o arquivo de texto as mensagens do chat
                        break;

                    case ProtocolSICmdType.USER_OPTION_3: //Trata da jogada executada utilizando assinaturas digitais
                                                          //Recebe o movimento cifrado
                        string move = security.DecifrarTexto(protocolSI.GetStringFromData());
                        //Espera pelo hash assinado do movimento cifrado com a chave privada
                        networkStream.Read(protocolSI.Buffer, 0, protocolSI.Buffer.Length);
                        while (protocolSI.GetCmdType() != ProtocolSICmdType.USER_OPTION_4)
                        {
                            networkStream.Read(protocolSI.Buffer, 0, protocolSI.Buffer.Length);
                        }
                        string moveSign = security.DecifrarTexto(protocolSI.GetStringFromData());
                        //Verifica a autenticidade do movimento
                        if (security.verifySignData(move, moveSign))
                        {
                            string[] coordenadas = move.Split('/');
                            int line = int.Parse(coordenadas[0]);
                            int col = int.Parse(coordenadas[1]);
                            string symbolPlayer = room.getNomeJogador(1) == this.nomeJogador ? "X" : "O";
                            switch (room.move(line, col, this.nomeJogador))
                            {
                                case -1: //Movimento é inválido
                                    msg = "Movimento inválido, tente novamente!";
                                    byte[] invalid = protocolSI.Make(ProtocolSICmdType.USER_OPTION_4, security.CifrarTexto(msg));
                                    networkStream.Write(invalid, 0, invalid.Length);
                                    break;

                                case 0: //Movimento válido
                                    broadcast(String.Format("{0}{1}/{2}", line, col, symbolPlayer), ProtocolSICmdType.USER_OPTION_5);
                                    break;

                                case 1: //Jogo termina com um ganhador
                                    broadcast(String.Format("{0}{1}/{2}", line, col, symbolPlayer), ProtocolSICmdType.USER_OPTION_5);
                                    Thread.Sleep(100);
                                    broadcast(String.Format("{0}/ganhou!", nomeJogador), ProtocolSICmdType.USER_OPTION_6);
                                    Thread.Sleep(100);
                                    if (trocaPosicao)
                                    {
                                        trocaDePosicao(room.isMultiplePlayers() == true ? true : false);
                                        trocaPosicao = false;
                                    }
                                    break;

                                case 2://Jogo termina em empate
                                    broadcast(String.Format("{0}{1}/{2}", line, col, symbolPlayer), ProtocolSICmdType.USER_OPTION_5);
                                    Thread.Sleep(100);
                                    broadcast(String.Format("/Empate!", nomeJogador), ProtocolSICmdType.USER_OPTION_6);
                                    Thread.Sleep(100);
                                    if (trocaPosicao)
                                    {
                                        trocaDePosicao(room.isMultiplePlayers() == true ? true : false);
                                        trocaPosicao = false;
                                    }
                                    break;

                                case 3: //Jogador incorreto tentou fazer o movimento
                                    msg = "Espere a sua vez!";
                                    byte[] jogadorIncorreto = protocolSI.Make(ProtocolSICmdType.USER_OPTION_4, security.CifrarTexto(msg));
                                    networkStream.Write(jogadorIncorreto, 0, jogadorIncorreto.Length);
                                    break;
                                default:
                                    Console.WriteLine("Algo de errado aconteceu ao executar room.move");
                                    break;
                            }
                        }
                        else
                        {
                            Console.WriteLine("Mensagem enviada inválida");
                            msg = "Ocorreu algum erro, tente novamente!";
                            byte[] invalid = protocolSI.Make(ProtocolSICmdType.USER_OPTION_7, security.CifrarTexto(msg));
                            networkStream.Write(invalid, 0, invalid.Length);
                        }
                        break;

                    case ProtocolSICmdType.USER_OPTION_5: //Jogador solicitou troca de posição
                        trocaPosicao = true;
                        if (!room.jogo.jogoComecou())
                        {
                            trocaDePosicao();
                        }
                        break;

                    case ProtocolSICmdType.USER_OPTION_6: //Jogador solicitou permitir vários jogadores
                        room.multiplePlayers();
                        msg = "Múltiplos jogadores habilitado";
                        broadcast(msg, ProtocolSICmdType.USER_OPTION_8);
                        break;

                    case ProtocolSICmdType.SECRET_KEY: //Recebe a senha do usuário
                        Console.WriteLine("Recebi a senha");
                        string senha = security.DecifrarTexto(protocolSI.GetStringFromData());
                        if (security.VerifyLogin(this.nomeJogador, senha))
                        { //Autentica o jogador
                            Console.WriteLine("{0} autenticado com sucesso", this.nomeJogador);
                            msg = String.Format("{0}/{1}", nomeJogador, security.GetPoints(nomeJogador));
                            byte[] ack = protocolSI.Make(ProtocolSICmdType.ACK, security.CifrarTexto(msg));
                            networkStream.Write(ack, 0, ack.Length);
                            jogadores = jogadores + nomeJogador + ',' + security.GetPoints(nomeJogador) + ';';
                        }
                        else
                        {
                            Console.WriteLine("{0} senha incorreta", this.nomeJogador);
                            byte[] msgConnection = protocolSI.Make(ProtocolSICmdType.USER_OPTION_3);
                            networkStream.Write(msgConnection, 0, msgConnection.Length);
                            esperaACK();
                        }
                        break;

                    case ProtocolSICmdType.EOT: //Finaliza a sessão do jogador
                        Console.WriteLine("Ending Thread from {0}", nomeJogador);
                        if (room != null)
                        {
                            security.setPoints(nomeJogador, room.getPontos(nomeJogador) + security.GetPoints(nomeJogador));
                            if (room.getClientList().Count >= 2)
                            {
                                msg = String.Format("Jogador {0} deixou a sala/{1}", nomeJogador, nomeJogador);
                                broadcast(msg, ProtocolSICmdType.USER_OPTION_9, nomeJogador);
                            }
                        }
                        room.novoJogo();
                        break;

                    case ProtocolSICmdType.ACK: //Caso no qual é feito um broadcast e a thread "errada" recebe o ACK e, portanto
                        connection.WaitOne();   //espera até que a thread "correta" receba o ACK para poder voltar a esperar nova mensagem
                        connection.ReleaseMutex();
                        break;

                    default:
                        break;
                }
            }
            networkStream.Close();
            this.tcpClient.Close();
            if (room != null)
            {
                this.room.removeClientOfRoom(this);
                this.room = null;
            }
        }
    }
}
