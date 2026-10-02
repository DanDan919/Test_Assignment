FROM postgres:18
# Patch Debian libraries while retaining PostgreSQL 18 and the existing data format.
RUN apt-get update && apt-get upgrade -y && rm -f /usr/local/bin/gosu && rm -rf /var/lib/apt/lists/*
# The official entrypoint uses gosu only to become the postgres user. Use the
# distribution's maintained setpriv instead of the old Go-based helper binary.
COPY --chmod=755 docker/postgres/run-as-postgres.sh /usr/local/bin/gosu
