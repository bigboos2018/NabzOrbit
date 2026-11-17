"""Linux/Mono integration check. Usage: python3 tests/loopback_tls.py /path/to/Nabz.Tests.exe
Uses a throwaway HOME and a loopback TLS server. No root, machine-store writes, network DNS or trust bypass.
"""
import os
import ssl
import sys
import tempfile
import subprocess
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def run():
    exe = Path(sys.argv[1]).resolve()
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        home = root / 'home'
        home.mkdir()
        env = dict(os.environ, HOME=str(home), XDG_CONFIG_HOME=str(home/'.config'), XDG_DATA_HOME=str(home/'.local/share'))
        def cmd(*args):
            subprocess.run(args, cwd=root, env=env, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        cmd('openssl','req','-x509','-newkey','rsa:2048','-nodes','-keyout','ca.key','-out','ca.crt','-subj','/CN=Disposable Orbit Test CA','-days','1','-addext','basicConstraints=critical,CA:TRUE')
        cmd('openssl','req','-newkey','rsa:2048','-nodes','-keyout','leaf.key','-out','leaf.csr','-subj','/CN=orbit-fixture.test')
        (root/'ext').write_text('subjectAltName=DNS:orbit-fixture.test\nbasicConstraints=CA:FALSE\nextendedKeyUsage=serverAuth\n')
        cmd('openssl','x509','-req','-in','leaf.csr','-CA','ca.crt','-CAkey','ca.key','-CAcreateserial','-out','leaf.crt','-days','1','-extfile','ext')
        cmd('cert-sync','--user',str(root/'ca.crt'))
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_HEAD(self):
                code = {'/ok':200,'/denied':403,'/redirect':302}.get(self.path,404)
                if self.headers.get('Host') != 'orbit-fixture.test' or self.headers.get('Authorization') or self.headers.get('Cookie'): code=400
                self.send_response(code)
                if code==302: self.send_header('Location','/ok')
                self.end_headers()
        with ThreadingHTTPServer(('127.0.0.1',0),Handler) as server:
            ctx=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
            ctx.load_cert_chain(root/'leaf.crt',root/'leaf.key')
            server.socket=ctx.wrap_socket(server.socket,server_side=True)
            t=threading.Thread(target=server.serve_forever,daemon=True);t.start()
            try:subprocess.run(['mono',str(exe),'--tls-loopback',str(server.server_port)],check=True,env=env,timeout=35)
            finally:server.shutdown();t.join(3)

if __name__=='__main__':run()
