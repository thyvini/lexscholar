#include <fcntl.h>
#include <stdio.h>

int main(void) {
    printf("O_CREAT=%d\nO_EXCL=%d\nO_RDWR=%d\nO_DIRECTORY=%d\n"
           "F_DUPFD=%d\nF_DUPFD_CLOEXEC=%d\nF_GETFD=%d\nFD_CLOEXEC=%d\n",
           O_CREAT, O_EXCL, O_RDWR, O_DIRECTORY, F_DUPFD, F_DUPFD_CLOEXEC,
           F_GETFD, FD_CLOEXEC);
    return 0;
}
