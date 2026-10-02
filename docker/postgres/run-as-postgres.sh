#!/bin/sh
set -eu
if [ "$#" -lt 2 ] || [ "$1" != postgres ]; then
    echo 'Only the postgres service identity is supported.' >&2
    exit 64
fi
shift
exec /usr/bin/setpriv --reuid=postgres --regid=postgres --init-groups "$@"
