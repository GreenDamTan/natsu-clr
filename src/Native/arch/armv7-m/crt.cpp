#ifndef _WIN32

#include <cstring>
#include <memory>
#include <stdio.h>
#include <sys/errno.h>
#include <sys/fcntl.h>
#include <sys/stat.h>
#include <sys/time.h>
#include <sys/times.h>
#include <sys/types.h>

void *HeapAlloc(size_t wantedSize) noexcept;
void HeapFree(void *ptr) noexcept;

extern "C"
{
    __attribute__((weak)) void *__dso_handle = 0;

    void *malloc(size_t n)
    {
        auto p = HeapAlloc(n);
        return p;
    }

    void free(void *p)
    {
        HeapFree(p);
    }

    void *realloc(void *p, size_t n)
    {
        auto np = malloc(n);
        if (p)
        {
            memcpy(np, p, n);
            free(p);
        }

        return np;
    }

    void *calloc(size_t num, size_t size)
    {
        const auto n = num * size;
        auto p = malloc(n);
        if (p)
            memset(p, 0, n);
        return p;
    }

    void *_malloc_r(struct _reent *, size_t n)
    {
        return malloc(n);
    }

    void _free_r(struct _reent *, void *p)
    {
        free(p);
    }

    void *_realloc_r(struct _reent *, void *p, size_t n)
    {
        return realloc(p, n);
    }

    void *_calloc_r(struct _reent *, size_t num, size_t size)
    {
        return calloc(num, size);
    }

#define STDIN_FILENO 0 /* standard input file descriptor */
#define STDOUT_FILENO 1 /* standard output file descriptor */
#define STDERR_FILENO 2 /* standard error file descriptor */

    int _close(int file) __attribute__((alias("close")));
    int _fstat(int file, struct stat *st) __attribute__((alias("fstat")));
    int _getpid() __attribute__((alias("getpid")));
    int _isatty(int file) __attribute__((alias("isatty")));
    int _kill(int pid, int sig) __attribute__((alias("kill")));
    int _lseek(int file, int ptr, int dir) __attribute__((alias("lseek")));
    int _open(const char *name, int flags, ...) __attribute__((alias("open")));
    int _read(int file, char *ptr, int len) __attribute__((alias("read")));
    int _write(int file, char *ptr, int len) __attribute__((alias("write")));
    int _gettimeofday(struct timeval *__restrict p, void *__restrict tz) __attribute__((alias("gettimeofday")));

    void _exit(int)
    {
        while (1)
            ;
    }

    int close(int file)
    {
        errno = ENOSYS;
        return -1;
    }

    char **environ; /* pointer to array of char * strings that define the current environment variables */
    int execve(const char *name, char *const *argv, char *const *env);
    int fork();

    int fstat(int file, struct stat *st)
    {
        errno = ENOSYS;
        return -1;
    }

    int getpid()
    {
        errno = ENOSYS;
        return -1;
    }

    int isatty(int file)
    {
        errno = ENOSYS;
        return 0;
    }

    int kill(int pid, int sig)
    {
        //g_Logger->PutString("kill\n");
        errno = ENOSYS;
        return -1;
    }

    int link(char *old, char *newl);

    int lseek(int file, int ptr, int dir)
    {
        errno = ENOSYS;
        return -1;
    }

    int open(const char *name, int flags, ...)
    {
        errno = ENOSYS;
        return -1;
    }

    int read(int file, char *ptr, int len)
    {
        errno = ENOSYS;
        return -1;
    }

    caddr_t sbrk(int incr);
    int stat(const char *file, struct stat *st);
    clock_t times(struct tms *buf);
    int unlink(char *name);
    int wait(int *status);

    int write(int file, char *ptr, int len)
    {
        if (file == STDOUT_FILENO || file == STDERR_FILENO)
        {
            //g_Logger->PutString({ ptr, size_t(len) });
            return len;
        }

        errno = ENOSYS;
        return -1;
    }

    int gettimeofday(struct timeval *__restrict p, void *__restrict tz)
    {
        errno = ENOSYS;
        return -1;
    }

    void *_sbrk(ptrdiff_t incr)
    {
        return nullptr;
    }
}

namespace __cxxabiv1
{
std::terminate_handler __terminate_handler = abort;
}
#endif
