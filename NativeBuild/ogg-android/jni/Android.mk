LOCAL_PATH := $(call my-dir)
ROOT := $(LOCAL_PATH)/../..

include $(CLEAR_VARS)
LOCAL_MODULE := ragnaogg
LOCAL_CFLAGS := -O3 -fPIC -Wall -Wno-unused-variable

LOCAL_C_INCLUDES := \
    $(ROOT)/ogg/include \
    $(ROOT)/vorbis/include \
    $(ROOT)/vorbis/lib

LOCAL_SRC_FILES := \
    $(ROOT)/ogg/src/bitwise.c \
    $(ROOT)/ogg/src/framing.c \
    $(ROOT)/vorbis/lib/analysis.c \
    $(ROOT)/vorbis/lib/bitrate.c \
    $(ROOT)/vorbis/lib/block.c \
    $(ROOT)/vorbis/lib/codebook.c \
    $(ROOT)/vorbis/lib/envelope.c \
    $(ROOT)/vorbis/lib/floor0.c \
    $(ROOT)/vorbis/lib/floor1.c \
    $(ROOT)/vorbis/lib/info.c \
    $(ROOT)/vorbis/lib/lookup.c \
    $(ROOT)/vorbis/lib/lpc.c \
    $(ROOT)/vorbis/lib/lsp.c \
    $(ROOT)/vorbis/lib/mapping0.c \
    $(ROOT)/vorbis/lib/mdct.c \
    $(ROOT)/vorbis/lib/psy.c \
    $(ROOT)/vorbis/lib/registry.c \
    $(ROOT)/vorbis/lib/res0.c \
    $(ROOT)/vorbis/lib/sharedbook.c \
    $(ROOT)/vorbis/lib/smallft.c \
    $(ROOT)/vorbis/lib/synthesis.c \
    $(ROOT)/vorbis/lib/vorbisenc.c \
    $(ROOT)/vorbis/lib/window.c \
    ragna_ogg.c

LOCAL_LDLIBS := -llog
include $(BUILD_SHARED_LIBRARY)
