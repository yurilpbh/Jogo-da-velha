﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Game;
using ClienteHandler;

namespace Room
{
    //Classe que trata dos elementos da sala do jogo
    public partial class room
    {
        private string name, jogador1, jogador2;
        private int pontosJog1, pontosJog2, pontosEmp;
        private List<string> proximoJogador = new List<string>();
        private List<ClientHandler> clientsList = new List<ClientHandler>();
        private Dictionary<string, int> pontuacaoJogadores = new Dictionary<string, int>();
        public Jogo jogo;
        private Boolean allowMultiplePlayers = false;

        public room(string name)
        {
            this.name = name;
            jogo = new Jogo();
            pontosJog1 = 0;
            pontosJog2 = 0;
            pontosEmp = 0;
        }

        public void addClientToRoom(ClientHandler client)
        {
            if (!clientsList.Contains(client))
            {
                clientsList.Add(client);
            }
        }

        public void removeClientOfRoom(ClientHandler client)
        {
            if (clientsList.Contains(client))
            {
                clientsList.Remove(client);
            }
        }

        public string getName()
        {
            return name;
        }

        public void setJogador(string nome)
        {
            if (jogador1 == null)
            {
                jogador1 = nome;
            }
            else if (jogador2 == null)
            {
                jogador2 = nome;
            }
            else
            {
                proximoJogador.Add(nome);
            }
            if (!pontuacaoJogadores.ContainsKey(nome))
            {
                pontuacaoJogadores.Add(nome, 0);
            }
        }
        public string getNomeJogador(int pos)
        {
            if (pos == 1)
            {
                return jogador1;
            }
            else if (pos == 2)
            {
                return jogador2;
            }
            else
            {
                return proximoJogador.ElementAt(3 - pos);
            }
        }

        public int getPosJogador(string nome)
        {
            if (nome == jogador1)
            {
                return 1;
            }
            else if (nome == jogador2)
            {
                return 2;
            }
            else
            {
                return 3 + proximoJogador.IndexOf(nome);
            }
        }

        public String getProximoJogadores()
        {
            String proxJogadores = "";
            foreach (var jogador in proximoJogador)
            {
                proxJogadores = proxJogadores + jogador + ",";
            }
            return proxJogadores;
        }

        public void trocaJogadores()
        {
            string aux = jogador1;
            jogador1 = jogador2;
            jogador2 = aux;
        }

        public Boolean isFull()
        {
            if (!allowMultiplePlayers)
            {
                if (clientsList.Count() == 2)
                {
                    return true;
                }
            }
            else if (clientsList.Count() == 10)
            {
                return true;
            }
            return false;
        }

        public Boolean multiplePlayers()
        {
            allowMultiplePlayers = true;
            return true;
        }

        public Boolean isMultiplePlayers()
        {
            return allowMultiplePlayers;
        }

        public List<ClientHandler> getClientList()
        {
            return clientsList;
        }

        public void writeLog(string msg) //Escreve o log do chat em um arquivo .txt
        {
            string path = @"logRoom-" + this.name + ".txt";
            File.AppendAllText(path, msg + Environment.NewLine, Encoding.UTF8);
        }

        public int move(int line, int col, string nome)
        {
            int posJogador = nome == jogador1 ? 1 : 2;
            switch (jogo.move(line, col, posJogador))
            {
                case -1: //Movimento é inválido
                    return -1;

                case 0: //Movimento válido
                    return 0;

                case 1: //Jogo termina com um ganhador
                    ganhador(posJogador);
                    novoJogo();
                    return 1;

                case 2://Jogo termina em empate
                    ganhador(3);
                    novoJogo();
                    return 2;

                case 3: //Jogador incorreto tentou fazer o movimento
                    return 3;

                default:
                    return 4;
            }
        }

        public void novoJogo()
        {
            jogo = new Jogo();
        }

        private void ganhador(int jogador)
        {
            switch (jogador)
            {
                case 1:
                    pontosJog1++;
                    if (allowMultiplePlayers)
                    {
                        proximoJogador.Add(jogador2);
                        pontuacaoJogadores[jogador2] = pontosJog2 + pontuacaoJogadores[jogador2];
                        jogador2 = proximoJogador.ElementAt(0);
                        proximoJogador.RemoveAt(0);
                        pontosJog2 = 0;
                        pontosEmp = 0;
                    }
                    break;
                case 2:
                    pontosJog2++;
                    if (allowMultiplePlayers)
                    {
                        proximoJogador.Add(jogador1);
                        pontuacaoJogadores[jogador1] = pontosJog1 + pontuacaoJogadores[jogador1];
                        jogador1 = proximoJogador.ElementAt(0);
                        proximoJogador.RemoveAt(0);
                        pontosJog1 = 0;
                        pontosEmp = 0;
                    }
                    break;
                case 3:
                    pontosEmp++;
                    break;
            }
        }
        public int getPontos(string quem)
        {
            if(!quem.Equals("empates"))
            {
                if (quem.Equals(jogador1))
                {
                    return pontuacaoJogadores[quem] + pontosJog1;
                }
                else if (quem.Equals(jogador2))
                {
                    return pontuacaoJogadores[quem] + pontosJog2;
                }
                return pontuacaoJogadores[quem];
            }
            else
            {
                return pontosEmp;
            }
        }
    }
}
