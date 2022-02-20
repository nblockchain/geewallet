#!/usr/bin/env bash
set -euxo pipefail

CONTNAME=snappy
IMGNAME=snapd
RELEASE=20.04

SUDO=""
if [ -z "$(id -Gn|grep docker)" ] && [ "$(id -u)" != "0" ]; then
    SUDO="sudo"
fi

if [ "$(which docker)" = "/snap/bin/docker" ]; then
    export TMPDIR="$(readlink -f ~/snap/docker/current)"
    # we need to run the snap once to have $SNAP_USER_DATA created
    /snap/bin/docker >/dev/null 2>&1
fi

BUILDDIR=$(mktemp -d)
# Copy repo contents to build dir
$SUDO cp -r $(pwd) $BUILDDIR/geewallet

usage() {
    echo "usage: $(basename $0) [options]"
    echo
    echo "  -c|--containername <name> (default: snappy)"
    echo "  -i|--imagename <name> (default: snapd)"
    rm_builddir
}

print_info() {
    echo
    echo "use: $SUDO docker exec -it $CONTNAME <command> ... to run a command inside this container"
    echo
    echo "to remove the container use: $SUDO docker rm -f $CONTNAME"
    echo "to remove the related image use: $SUDO docker rmi $IMGNAME"
}

clean_up() {
    sleep 1
    $SUDO docker rm -f $CONTNAME >/dev/null 2>&1 || true
    $SUDO docker rmi $IMGNAME >/dev/null 2>&1 || true
    $SUDO docker rmi $($SUDO docker images -f "dangling=true" -q) >/dev/null 2>&1 || true
    rm_builddir
}

rm_builddir() {
    rm -rf $BUILDDIR || true
    exit 0
}

trap clean_up 1 2 3 4 9 15

while [ $# -gt 0 ]; do
    case "$1" in
        -c|--containername)
            [ -n "$2" ] && CONTNAME=$2 shift || usage
            ;;
        -i|--imagename)
            [ -n "$2" ] && IMGNAME=$2 shift || usage
            ;;
        -h|--help)
            usage
            ;;
        *)
            usage
            ;;
    esac
    shift
done

if [ -n "$($SUDO docker ps -f name=$CONTNAME -q)" ]; then
    echo "Container $CONTNAME already running!"
    print_info
    rm_builddir
fi

if [ -z "$($SUDO docker images|grep $IMGNAME)" ]; then
    cat << EOF > $BUILDDIR/Dockerfile
FROM ubuntu:$RELEASE
ENV container docker
ENV PATH "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/snap/bin"
ENV LANG C.UTF-8
ENV LC_ALL C.UTF-8
RUN apt update &&\
 DEBIAN_FRONTEND=noninteractive\
 apt install -y snapd=2.51.1+20.04ubuntu2
 apt install -y fuse snap-confine squashfuse sudo init lsb-release git docker.io build-essential pkg-config curl &&\
 apt install -y libgtk2.0-cil-dev &&\
 apt clean &&\
 dpkg-divert --local --rename --add /sbin/udevadm &&\
 ln -s /bin/true /sbin/udevadm
RUN systemctl enable snapd
VOLUME ["/sys/fs/cgroup"]
STOPSIGNAL SIGRTMIN+3
RUN mkdir -p /geewallet
WORKDIR /geewallet
ADD geewallet /geewallet
CMD ["/sbin/init"]
EOF
    $SUDO docker build -t $IMGNAME --force-rm=true --rm=true $BUILDDIR || clean_up
fi

# start the detached container
$SUDO docker run \
    --name=$CONTNAME \
    -ti \
    --tmpfs /run \
    --tmpfs /run/lock \
    --tmpfs /tmp \
    --cap-add SYS_ADMIN \
    --device=/dev/fuse \
    --security-opt apparmor:unconfined \
    --security-opt seccomp:unconfined \
    -v /sys/fs/cgroup:/sys/fs/cgroup:ro \
    -v /lib/modules:/lib/modules:ro \
    -d $IMGNAME || clean_up

# wait for snapd to start
TIMEOUT=100
SLEEP=0.1
echo -n "Waiting up to $(($TIMEOUT/10)) seconds for snapd startup "
while [ "$($SUDO docker exec $CONTNAME sh -c 'systemctl status snapd.seeded >/dev/null 2>&1; echo $?')" != "0" ]; do
    echo -n "."
    sleep $SLEEP || clean_up
    if [ "$TIMEOUT" -le "0" ]; then
        echo " Timed out!"
        clean_up
    fi
    TIMEOUT=$(($TIMEOUT-1))
done
echo " done"

$SUDO docker exec $CONTNAME snap install core || clean_up
echo "container $CONTNAME started ..."

$SUDO docker exec $CONTNAME scripts/install_mono_from_microsoft_deb_packages.sh

print_info
rm_builddir
