#include<vector>
#include "equihashverify.h"

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" MODULE_API bool equihash_verify_new_export(const char* header, int header_length, const char* solution, int solution_length)
{

    if(header_length != 140 || solution_length != 1344) {
        return false;
    }

    std::vector<unsigned char> vecSolution(solution, solution + solution_length);

    return verifyEH(header, vecSolution);
}

