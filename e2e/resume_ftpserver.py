"""Local FTP fault injection for Ferry's resume regression checks."""
import logging
import os
import socket
import struct
import sys
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler, DTPHandler
from pyftpdlib.log import config_logging
from pyftpdlib.servers import FTPServer

port, root, fault = int(sys.argv[1]), sys.argv[2], sys.argv[3]
attempts = 0
connections = 0


def record(line):
    with open(os.path.join(root, "commands.txt"), "a", encoding="utf-8") as f:
        f.write(line + "\n")


class Data(DTPHandler):
    def handle_read(self):
        super().handle_read()
        if (fault == "reset" and "a" in self.file_obj.mode and attempts <= 2
                and self.tot_bytes_received >= 2 * 1024 * 1024 and not self._closed):
            record("RESET " + str(self.tot_bytes_received))
            self.file_obj.flush()
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER,
                                   struct.pack("hh" if os.name == "nt" else "ii", 1, 0))
            channel = self.cmd_channel
            self.close()
            channel.close()

    handle_read_event = handle_read


class Handler(FTPHandler):
    def on_connect(self):
        global connections
        connections += 1
        record("OPEN " + str(connections))

    def on_disconnect(self):
        global connections
        connections -= 1
        record("CLOSE " + str(connections))

    def ftp_APPE(self, path):
        global attempts
        attempts += 1
        record("APPE " + os.path.basename(path))
        if fault == "reject":
            self.respond("502 APPE not implemented")
        elif fault == "temporary" and attempts <= 2:
            self.respond("450 Temporarily unavailable")
        elif fault == "denied":
            self.respond("550 Permission denied")
        else:
            return super().ftp_APPE(path)

    def ftp_STOR(self, path, mode="w"):
        if mode == "w":
            record("STOR " + os.path.basename(path))
        return super().ftp_STOR(path, mode)

    def pre_process_command(self, line, cmd, arg):
        if cmd in {"FEAT", "SELF"} or (cmd == "APPE" and fault == "noappe"):
            self.respond("502 Command not recognized")
            return
        return super().pre_process_command(line, cmd, arg)


config_logging(level=logging.WARNING)
auth = DummyAuthorizer()
auth.add_user("ps5", "ps5pass", root, perm="elradfmwMT")
Handler.authorizer = auth
Handler.dtp_handler = Data
FTPServer(("127.0.0.1", port), Handler).serve_forever()
