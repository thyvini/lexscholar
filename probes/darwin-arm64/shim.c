#include <fcntl.h>
#include <sys/types.h>

int probe_open(const char *path, int flags, unsigned int mode) {
    return open(path, flags, (mode_t)mode);
}

int probe_fcntl(int fd, int command, int argument) {
    return fcntl(fd, command, argument);
}
