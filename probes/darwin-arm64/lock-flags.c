#include <fcntl.h>
#include <unistd.h>

int probe_cloexec(int fd) {
    int flags = fcntl(fd, F_GETFD);
    return flags < 0 ? -1 : !!(flags & FD_CLOEXEC);
}

int probe_reference(const char *path) {
    int fd = open(path, O_RDWR | O_CREAT | O_EXCL | O_CLOEXEC, 0600);
    if (fd < 0) return -1;
    int duplicate = dup(fd);
    if (duplicate < 0) { close(fd); return -1; }
    int original_flag = probe_cloexec(fd);
    int duplicate_flag = probe_cloexec(duplicate);
    close(duplicate);
    close(fd);
    if (original_flag < 0 || duplicate_flag < 0) return -1;
    return original_flag * 2 + duplicate_flag;
}
