#! /bin/sh

set -e

usage()
{
    echo "Usage: compile <arch> <TargetOS>"
    echo "archs: i686,x86_64,armv7,aarch64"
    echo "os: win10"
}

using()
{
    echo "preparing for MSVC target: $MSVC_TUPLE"
}

if [ "$1" != "" ]; then

case "$1" in

i686)
    MSVC_TUPLE="Win32"
    using
    ;;
x86_64)
    MSVC_TUPLE="x64"
    using
    ;;
armv7)
    MSVC_TUPLE="ARM"
    using
    ;;
aarch64)
    MSVC_TUPLE="ARM64"
    using
    ;;
*) echo "Unknown arch: $1"
   usage
   exit 1
   ;;
esac

case "$2" in
    win10)
        WINVER=0xA00
        RUNTIME=ucrt
        RUNTIME_EXTRA='-lvcruntime140_app'
        LIBKERNEL32='-lwindowsapp'
        LIBLOLE32=
        ;;
    win81)
        echo "win81 not supported anymore"
        usage
        exit 1
        #~ WINVER=0x602
        #~ RUNTIME=msvcr120_app
        #~ LIBKERNEL32=-lkernel32
        #~ LIBLOLE32=-lole32
        ;;
    *)
        echo "Unknown OS: $2"
        usage
        exit 1
        ;;
esac

# 1/ libvlc, libvlccore and its plugins
TESTED_HASH=45df8a6415
if [ ! -d "vlc" ]; then
    echo "VLC source not found, cloning"
    git clone http://git.videolan.org/git/vlc/vlc-3.0.git vlc
    cd vlc
    git config --global user.email "cone@example.com"
    git config --local user.name "Cony Cone"
    git am -3 ../patches/*.patch
    if [ $? -ne 0 ]; then
        git am --abort
        echo "Applying the patches failed, aborting git-am"
        exit 1
    fi
else
    echo "VLC source found"
    cd vlc
    if ! git cat-file -e ${TESTED_HASH}; then
        cat << EOF
***
*** Error: Your vlc checkout does not contain the latest tested commit ***
***

EOF
        exit 1
    fi
fi

MAKEFLAGS=
if which nproc >/dev/null
then
MAKEFLAGS=-j`nproc`
fi

TARGET_TUPLE=${1}-w64-mingw32
case "${1}" in
    *)
        COMPILER=${TARGET_TUPLE}-gcc
        COMPILERXX=${TARGET_TUPLE}-g++
        if ${COMPILER} --version | grep -q gcc ; then
            HAS_GCC=1
        else
            HAS_CLANG=1
        fi
        if [ "${HAS_GCC}" = "1" ]; then
            ${COMPILER} -dumpspecs | sed -e "s/-lmingwex/-lwinstorecompat -lmingwex -lwinstorecompat $LIBLOLE32 -lruntimeobject -lsynchronization/" -e "s/-lmsvcrt/$RUNTIME_EXTRA -l$RUNTIME/" -e "s/-lkernel32/$LIBKERNEL32/" > ../newspecfile
            NEWSPECFILE="`pwd`/../newspecfile"
            COMPILER="${COMPILER} -specs=$NEWSPECFILE"
            COMPILERXX="${COMPILERXX} -specs=$NEWSPECFILE"
        fi
        BUILD_ARCH=`gcc -dumpmachine`
        ;;
esac

# Build tools with the native compiler
echo "Compiling missing tools..."
cd extras/tools

export PATH="$PWD/build/bin":"$PATH"
# Force patched meson as newer versions don't add -lpthread properly in libplacebo.pc
FORCED_TOOLS="meson"
if [ "${HAS_CLANG}" = "1" ] ; then
    # We need a patched version of libtool & cmake, regardless of which
    # version is installed on the system.
    # cmake can go away when we switch to 3.13.0
    FORCED_TOOLS="$FORCED_TOOLS libtool"
fi
NEEDED="$FORCED_TOOLS" ./bootstrap && make $MAKEFLAGS
cd ../../

EXTRA_CPPFLAGS="-D_WIN32_WINNT=$WINVER -DWINVER=$WINVER -DWINSTORECOMPAT -D_UNICODE -DUNICODE -DWINAPI_FAMILY=WINAPI_FAMILY_APP"
if [ "${HAS_GCC}" = 1 ]; then
    EXTRA_LDFLAGS="-lnormaliz -lwinstorecompat -lruntimeobject"
else
    # Clang doesn't support spec files, but will skip the builtin -lmsvcrt and -lkernel32 etc if it detects -lmsvcr* or -lucrt*, and
    # -lwindowsapp on the command line.
    EXTRA_LDFLAGS="-lnormaliz -lwinstorecompat $LIBOLE32 -lruntimeobject -lsynchronization $RUNTIME_EXTRA -l$RUNTIME $LIBKERNEL32"
fi

echo "Building the contribs"
CONTRIB_FOLDER=contrib/winrt-$1-$RUNTIME
mkdir -p $CONTRIB_FOLDER
cd $CONTRIB_FOLDER
../bootstrap --host=${TARGET_TUPLE} --build=$BUILD_ARCH --disable-disc \
    --disable-sdl \
    --disable-schroedinger \
    --disable-vncserver \
    --disable-chromaprint \
    --disable-modplug \
    --disable-SDL_image \
    --disable-fontconfig \
    --enable-zvbi \
    --disable-caca \
    --disable-gettext \
    --enable-gme \
    --enable-vorbis \
    --enable-mad \
    --enable-sidplay2 \
    --enable-samplerate \
    --enable-iconv \
    --disable-goom \
    --enable-dca \
    --disable-fontconfig \
    --disable-gpg-error \
    --disable-projectM \
    --enable-ass \
    --disable-qt \
    --disable-qtsvg \
    --disable-aribb25 \
    --disable-gnuv3 \
    --enable-ssh2 \
    --disable-vncclient \
    --enable-jpeg \
    --enable-postproc \
    --enable-vpx \
    --enable-libdsm \
    --disable-x264 \
    --disable-x265 \
    --disable-srt \
    --disable-aom

echo "EXTRA_CFLAGS=${EXTRA_CPPFLAGS}" >> config.mak
echo "EXTRA_LDFLAGS=${EXTRA_LDFLAGS}" >> config.mak
echo "HAVE_WINSTORE := 1" >> config.mak
echo "CC=${COMPILER}" >> config.mak
echo "CXX=${COMPILERXX}" >> config.mak
echo "MAKEFLAGS=${MAKEFLAGS}" >> config.mak
export PKG_CONFIG_LIBDIR="`pwd`/../${TARGET_TUPLE}/lib/pkgconfig"

USE_FFMPEG=1 make || USE_FFMPEG=1 make -j1

BUILD_FOLDER=winrt-$1-$RUNTIME
cd ../.. && mkdir -p ${BUILD_FOLDER} && cd ${BUILD_FOLDER}

echo "Bootstraping"
../bootstrap

echo "Configuring"
CPPFLAGS="${EXTRA_CPPFLAGS}" \
LDFLAGS="${EXTRA_LDFLAGS}" \
CC="${COMPILER}" \
CXX="${COMPILERXX}" \
ac_cv_search_connect="-lws2_32" \
../../configure.sh --host=${TARGET_TUPLE}

echo "Building"
make $MAKEFLAGS

echo "Package"
make install

rm -rf tmp && mkdir tmp

# Compiler shared DLLs, when using compilers built with --enable-shared
# The shared DLLs may not necessarily be in the first LIBRARY_PATH, we
# should check them all.
library_path_list=`${TARGET_TUPLE}-g++ -v /dev/null 2>&1 | grep ^LIBRARY_PATH|cut -d= -f2` ;

find _win32/bin -name "*.dll" -exec cp -v {} tmp \;
cp -r _win32/include tmp/
cp -r _win32/lib/vlc/plugins tmp/

find tmp -name "*.la" -exec rm -v {} \;
find tmp -name "*.a" -exec rm -v {} \;
blocklist="
wingdi
waveout
dshow
directdraw
windrive
globalhotkeys
direct2d
ntservice
dxva2
dtv
vcd
cdda
quicktime
atmo
oldrc
dmo
panoramix
screen
win_msg
win_hotkeys
crystalhd
smb
"
regexp=
for i in ${blocklist}
do
    if [ -z "${regexp}" ]
    then
        regexp="${i}"
    else
        regexp="${regexp}|${i}"
    fi
done
rm `find tmp/plugins -name 'lib*plugin.dll' | grep -E "lib(${regexp})_plugin.dll"`

find tmp \( -name "*.dll" -o -name "*.exe" \) -exec ${TARGET_TUPLE}-strip {} \;
find tmp \( -name "*.dll" -o -name "*.exe" \) -exec ../../appcontainer.pl {} \;

cp lib/.libs/libvlc.dll.a tmp/libvlc.lib
cp src/.libs/libvlccore.dll.a tmp/libvlccore.lib

CURRENTDATE="$(date +%Y%m%d-%H%M)"

cd tmp
7z a ../vlc-${1}-${2}-${CURRENTDATE}.7z *
cd ..
rm -rf tmp

else
    usage
fi
