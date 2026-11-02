"""Signs a vault account in to the local vault through the real sign-in (password, then a real
TOTP code) and writes a Netscape cookie jar that `curl -b` accepts. An account that has never
signed in is enrolled on the way, with the authenticator secret the enrollment page creates.

Usage:  python3 -I signin.py <username> <cookie-jar-path>
Env:    EV_PASSWORD      the account's password
        EV_DB_ENGINE     where the vault keeps its accounts: postgres (default) or sqlserver, any case
        EV_PG_CONTAINER  the PostgreSQL container (default: eclipsvault-postgres)
        EV_PG_USER       its user (default: eclipsvault, as in docker-compose.yml)
        EV_PG_PASSWORD   its password (default: dev-postgres-password, as in docker-compose.yml)
        EV_SQL_CONTAINER the SQL Server container (default: eclipsvault-db)
        EV_SQL_PASSWORD  the SQL Server sa password
        EV_BASE_URL      the vault (default: https://localhost:7443)

Exits non-zero, saying why, when the sign-in does not end in a session.
"""
import base64, hashlib, hmac, http.cookiejar, os, re, ssl, struct, subprocess, sys, time
import urllib.parse, urllib.request

BASE = os.environ.get("EV_BASE_URL", "https://localhost:7443")
ENGINE = os.environ.get("EV_DB_ENGINE", "postgres").lower()
DATABASE = "EclipsVaultUmbraDb"


def run_secret_query(command, sql=None, env=None):
    # Exits with the tool's own error, not a traceback that would print the whole command.
    result = subprocess.run(command, input=sql, env={**os.environ, **(env or {})}, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"Could not read the TOTP secret: {result.stderr.strip()}")
    return result.stdout.strip()


def postgres_totp_secret(username):
    # psql quotes :'username' as a string literal, so the name stays data and never becomes SQL.
    return run_secret_query(
        ["docker", "exec", "-i", "-e", "PGPASSWORD", os.environ.get("EV_PG_CONTAINER", "eclipsvault-postgres"),
         "psql", "-X", "-q", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-h", "127.0.0.1",
         "-U", os.environ.get("EV_PG_USER", "eclipsvault"), "-d", DATABASE, "-v", f"username={username}"],
        sql="SELECT \"TotpSecret\" FROM \"Users\" WHERE \"Username\" = :'username';\n",
        env={"PGPASSWORD": os.environ.get("EV_PG_PASSWORD", "dev-postgres-password")})


def sql_server_totp_secret(username):
    # sqlcmd variables are textual substitution, so this relies on main() having checked the name.
    # -b and -r1 make a failed query exit non-zero with its error on stderr, where it gets reported.
    return run_secret_query(
        ["docker", "exec", "-e", "SQLCMDPASSWORD", os.environ.get("EV_SQL_CONTAINER", "eclipsvault-db"),
         "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-C", "-b", "-r1",
         "-d", DATABASE, "-h", "-1", "-W", "-v", f"user={username}",
         "-Q", "SET NOCOUNT ON; SELECT TotpSecret FROM Users WHERE Username = '$(user)'"],
        env={"SQLCMDPASSWORD": os.environ["EV_SQL_PASSWORD"]})


def totp_secret(username):
    read = postgres_totp_secret if ENGINE == "postgres" else sql_server_totp_secret
    secret = read(username)
    if not re.fullmatch(r"[A-Z2-7]+=*", secret):
        sys.exit(f"No TOTP secret found for {username!r}.")
    return secret


def totp(secret):
    # A code is accepted once per 30-second step (replay protection), so never use one about to expire.
    if 30 - time.time() % 30 < 5:
        time.sleep(6)
    digest = hmac.new(base64.b32decode(secret), struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
    offset = digest[-1] & 15
    return f"{(struct.unpack('>I', digest[offset:offset + 4])[0] & 0x7fffffff) % 1000000:06d}"


def main():
    username, jar_path = sys.argv[1:3]
    # These checks come before the sign-in, so a mistake spends none of its throttle budget.
    if ENGINE not in ("postgres", "sqlserver"):
        sys.exit(f"EV_DB_ENGINE must be postgres or sqlserver, not {ENGINE!r}.")
    if ENGINE == "sqlserver" and "EV_SQL_PASSWORD" not in os.environ:
        sys.exit("EV_DB_ENGINE=sqlserver needs EV_SQL_PASSWORD, the sa password.")
    # Also checked before any query sees the name.
    if not re.fullmatch(r"[A-Za-z0-9._@-]+", username):
        sys.exit(f"Refusing an unusual username: {username!r}")
    # Certificate checks are off only for a loopback vault, whose dev certificate is self-signed.
    if urllib.parse.urlsplit(BASE).hostname not in ("localhost", "127.0.0.1", "::1"):
        sys.exit(f"Refusing {BASE}: this script is for a local vault only.")
    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(jar),
        urllib.request.HTTPSHandler(context=ssl._create_unverified_context()))

    def post_form(path, fields):
        page = opener.open(BASE + path).read().decode()
        token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page).group(1)
        return opener.open(BASE + path, urllib.parse.urlencode({**fields, "__RequestVerificationToken": token}).encode())

    code_step = urllib.parse.urlsplit(
        post_form("/Account/Login", {"Username": username, "Password": os.environ["EV_PASSWORD"]}).geturl()).path
    if code_step not in ("/Account/Totp", "/Account/EnrollTotp"):
        sys.exit("The password step did not reach the TOTP page: wrong password, a locked account, or the sign-in throttle.")
    # Read only now: a first sign-in's secret exists once the enrollment page has been shown.
    secret = totp_secret(username)
    landed = post_form(code_step, {"Code": totp(secret)}).geturl()
    if "/Account/" in landed or not any(c.name == "EclipsVault.Session" for c in jar):
        sys.exit(f"The TOTP step did not create a session (ended at {landed}).")

    # Written by hand: Python's MozillaCookieJar saves `localhost` as `localhost.local`, which curl never sends.
    with open(jar_path, "w") as f:
        f.write("# Netscape HTTP Cookie File\n")
        for c in jar:
            f.write(f"{c.domain.removesuffix('.local')}\tFALSE\t{c.path}\t{'TRUE' if c.secure else 'FALSE'}\t0\t{c.name}\t{c.value}\n")
    print(f"Signed in as {username}; landed on {landed}; cookies in {jar_path}")


main()
