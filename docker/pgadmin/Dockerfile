FROM dpage/pgadmin4:9.18
USER root
# Patch the runtime HTTP library. pip's vendored build tools are not needed
# by the official pgAdmin entrypoint, so remove pip from the runtime image.
RUN apk upgrade --no-cache && \
    /venv/bin/python -m pip install --no-cache-dir urllib3==2.8.0 && \
    /venv/bin/python -m pip check && \
    /usr/local/bin/python -m pip uninstall -y pip
USER 5050
