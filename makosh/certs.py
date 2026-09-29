from __future__ import annotations

import datetime
import ipaddress
import socket
from pathlib import Path

from makosh import config


def lan_ips() -> list[str]:
    found = {"127.0.0.1"}
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            found.add(info[4][0])
    except OSError:
        pass
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.connect(("8.8.8.8", 80))
        found.add(sock.getsockname()[0])
    except OSError:
        pass
    finally:
        sock.close()
    return sorted(ip for ip in found if not ip.startswith("169.254."))


def ensure_certs() -> tuple[Path, Path]:
    folder = config.DATA_DIR / "certs"
    folder.mkdir(parents=True, exist_ok=True)
    cert_path = folder / "cert.pem"
    key_path = folder / "key.pem"
    if cert_path.is_file() and key_path.is_file():
        return cert_path, key_path

    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID

    now = datetime.datetime.now(datetime.timezone.utc)
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    names = [
        x509.DNSName("localhost"),
        x509.IPAddress(ipaddress.IPv4Address("127.0.0.1")),
    ]
    for ip in lan_ips():
        names.append(x509.IPAddress(ipaddress.IPv4Address(ip)))
    subject = issuer = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "Makosh")])
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - datetime.timedelta(minutes=5))
        .not_valid_after(now + datetime.timedelta(days=825))
        .add_extension(x509.SubjectAlternativeName(names), critical=False)
        .sign(key, hashes.SHA256())
    )
    key_path.write_bytes(
        key.private_bytes(
            encoding=serialization.Encoding.PEM,
            format=serialization.PrivateFormat.TraditionalOpenSSL,
            encryption_algorithm=serialization.NoEncryption(),
        )
    )
    cert_path.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    return cert_path, key_path
