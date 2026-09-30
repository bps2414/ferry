# Servidor FTP local do E2E, imitando o ftpsrv do PS5: só os comandos que ele implementa
# (lista do main.c do ps5-payload-ftpsrv) e "502 Command not recognized" para o resto.
# Upload limitado (~40 MB/s por conexão) para simular rede e dar tempo de pausar no meio.
# 3º argumento: "appe" (versões novas do ftpsrv/etaHEN) ou "noappe" (versão do repositório).
import logging, sys
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler, ThrottledDTPHandler
from pyftpdlib.log import config_logging
from pyftpdlib.servers import FTPServer

port, root, mode = int(sys.argv[1]), sys.argv[2], sys.argv[3]
FTPSRV = {"CDUP", "CWD", "DELE", "LIST", "MKD", "NOOP", "PASV", "PORT", "PWD", "QUIT", "REST", "RETR",
          "RMD", "RNFR", "RNTO", "SIZE", "STOR", "SYST", "TYPE", "USER", "PASS"}
if mode == "appe":
    FTPSRV.add("APPE")

class Ftpsrv(FTPHandler):
    def pre_process_command(self, line, cmd, arg):
        if cmd not in FTPSRV:
            with open(root + ".recusados.txt", "a", encoding="utf-8") as f:
                f.write(line + "\n")
            self.respond("502 Command not recognized")
            return
        return super().pre_process_command(line, cmd, arg)

config_logging(level=logging.INFO)
auth = DummyAuthorizer()
auth.add_user("ps5", "ps5pass", root, perm="elradfmwMT")
ThrottledDTPHandler.read_limit = 40 * 1024 * 1024
Ftpsrv.authorizer = auth
Ftpsrv.dtp_handler = ThrottledDTPHandler
FTPServer(("127.0.0.1", port), Ftpsrv).serve_forever()
