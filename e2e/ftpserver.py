# Servidor FTP local do E2E, imitando o ftpsrv do PS5: só os comandos que ele implementa
# (lista do main.c do ps5-payload-ftpsrv) e "502 Command not recognized" para o resto.
# Upload limitado (~40 MB/s por conexão) para simular rede e dar tempo de pausar no meio.
# 3º argumento: "appe" (ftpsrv novo do ps5-payload-dev / etaHEN) ou "noappe" (versão antiga do john-tornblom).
# O "appe" imita também o SELF do ftpsrv novo: ligado por padrão, faz o SIZE de um SELF (aqui: eboot.bin)
# devolver o tamanho do ELF de dentro (aqui: 1 byte a menos). O comando SELF liga/desliga.
import logging, os, sys
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler, ThrottledDTPHandler
from pyftpdlib.log import config_logging
from pyftpdlib.servers import FTPServer

port, root, mode = int(sys.argv[1]), sys.argv[2], sys.argv[3]
FTPSRV = {"CDUP", "CWD", "DELE", "LIST", "MKD", "NOOP", "PASV", "PORT", "PWD", "QUIT", "REST", "RETR",
          "RMD", "RNFR", "RNTO", "SIZE", "STOR", "SYST", "TYPE", "USER", "PASS"}
if mode == "appe":
    FTPSRV |= {"APPE", "SELF"}

class Ftpsrv(FTPHandler):
    proto_cmds = dict(FTPHandler.proto_cmds, SELF=dict(perm=None, auth=True, arg=False, help="Syntax: SELF"))
    self2elf = True

    def pre_process_command(self, line, cmd, arg):
        if cmd not in FTPSRV:
            with open(root + ".recusados.txt", "a", encoding="utf-8") as f:
                f.write(line + "\n")
            self.respond("502 Command not recognized")
            return
        return super().pre_process_command(line, cmd, arg)

    def ftp_SELF(self, line):
        self.self2elf = not self.self2elf
        self.respond("226 SELF transfer mode " + ("enabled" if self.self2elf else "disabled"))

    def ftp_SIZE(self, path):
        if mode == "appe" and self.self2elf and os.path.basename(path).lower() == "eboot.bin" and os.path.isfile(path):
            self.respond("213 %d" % (os.path.getsize(path) - 1))
            return
        return super().ftp_SIZE(path)

config_logging(level=logging.INFO)
auth = DummyAuthorizer()
auth.add_user("ps5", "ps5pass", root, perm="elradfmwMT")
ThrottledDTPHandler.read_limit = 40 * 1024 * 1024
Ftpsrv.authorizer = auth
Ftpsrv.dtp_handler = ThrottledDTPHandler
FTPServer(("127.0.0.1", port), Ftpsrv).serve_forever()
