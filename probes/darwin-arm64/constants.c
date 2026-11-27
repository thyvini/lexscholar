#include <fcntl.h>
#include <errno.h>
#include <stdio.h>
#include <sys/file.h>

int main(void) {
    printf("O_CREAT=%d\nO_EXCL=%d\nO_RDWR=%d\nO_DIRECTORY=%d\n"
           "O_NOFOLLOW=%d\nO_CLOEXEC=%d\n"
           "F_DUPFD=%d\nF_DUPFD_CLOEXEC=%d\nF_GETFD=%d\nFD_CLOEXEC=%d\n"
           "LOCK_EX=%d\nLOCK_NB=%d\nLOCK_UN=%d\nEWOULDBLOCK=%d\nELOOP=%d\n",
           O_CREAT, O_EXCL, O_RDWR, O_DIRECTORY, O_NOFOLLOW, O_CLOEXEC,
           F_DUPFD, F_DUPFD_CLOEXEC, F_GETFD, FD_CLOEXEC,
           LOCK_EX, LOCK_NB, LOCK_UN, EWOULDBLOCK, ELOOP);
    return 0;
}
