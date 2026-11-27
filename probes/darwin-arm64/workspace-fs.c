#include <stdio.h>
#include <sys/mount.h>

int main(int argc, char **argv) {
    struct statfs info;
    if (argc != 2 || statfs(argv[1], &info) != 0) return 1;
    printf("workspace filesystem type=%s mount=%s\n", info.f_fstypename, info.f_mntonname);
    return 0;
}
