#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <time.h>
#include <string.h>

#include "tiny_sha3/sha3.h"

#ifdef _MSC_VER
#include <malloc.h>
#endif

#define HEADER_SIZE 80
#define HASH_OUT_SIZE 32
#define P0_SIZE 64
#define N_ITER 8
#define N_SUBSET P0_SIZE*N_ITER
#define N_ROT 32
#define N_INDEXES 4096
#define BYTE_ALIGNMENT 16

#define NODE_SIZE 32

const char* input_header_hex = "000000203a297b4b7685170d7644b43e5a6056234cc2414edde454a87580e1967d14c1078c13ea916117b0608732f3f65c2e03b81322efc0a62bcee77d8a9371261970a58a5a715da80e031b02560ad8";
int blob_initialized = 0;
unsigned char* blob_bytes;
size_t blob_size;

////////////////////////////
// Data file generation

struct Graph
{
    FILE *db;
    int64_t log2;
    int64_t pow2;
    uint8_t *pk;
    int64_t index;
};

int64_t Log2(int64_t x)
{
    int64_t r = 0;
    for (; x > 1; x >>= 1)
    {
        r++;
    }

    return r;
}

int64_t bfsToPost(struct Graph *g, const int64_t node)
{
    return node & ~g->pow2;
}

int64_t numXi(int64_t index)
{
    return (1 << ((uint64_t)index)) * (index + 1) * index;
}

void WriteId(struct Graph *g, uint8_t *Node, const int64_t id)
{
    fseek(g->db, id * NODE_SIZE, SEEK_SET);
    fwrite(Node, 1, NODE_SIZE, g->db);
}

void WriteNode(struct Graph *g, uint8_t *Node, const int64_t id)
{
    const int64_t idx = bfsToPost(g, id);
    WriteId(g, Node, idx);
}

void NewNode(struct Graph *g, const int64_t id, uint8_t *hash)
{
    WriteNode(g, hash, id);
}

uint8_t *GetId(struct Graph *g, const int64_t id)
{
    fseek(g->db, id * NODE_SIZE, SEEK_SET);
    uint8_t *node = (uint8_t *)malloc(NODE_SIZE);
    const size_t bytes_read = fread(node, 1, NODE_SIZE, g->db);
    if(bytes_read != NODE_SIZE) {
        return NULL;
    }
    return node;
}

uint8_t *GetNode(struct Graph *g, const int64_t id)
{
    const int64_t idx = bfsToPost(g, id);
    return GetId(g, idx);
}

uint32_t WriteVarInt(uint8_t *buffer, int64_t val)
{
    memset(buffer, 0, NODE_SIZE);
    uint64_t uval = ((uint64_t)(val)) << 1;
    if (val < 0)
    {
        uval = ~uval;
    }
    uint32_t i = 0;
    while (uval >= 0x80)
    {
        buffer[i] = (uint8_t)uval | 0x80;
        uval >>= 7;
        i++;
    }
    buffer[i] = (uint8_t)uval;
    return i;
}

void ButterflyGraph(struct Graph *g, int64_t index, int64_t *count)
{
    if (index == 0)
    {
        index = 1;
    }

    int64_t numLevel = 2 * index;
    int64_t perLevel = (int64_t)(1 << (uint64_t)index);
    int64_t begin = *count - perLevel;
    int64_t level, i;

    for (level = 1; level < numLevel; level++)
    {
        for (i = 0; i < perLevel; i++)
        {
            int64_t prev;
            int64_t shift = index - level;
            if (level > numLevel / 2)
            {
                shift = level - numLevel / 2;
            }
            if (((i >> (uint64_t)shift) & 1) == 0)
            {
                prev = i + (1 << (uint64_t)shift);
            }
            else
            {
                prev = i - (1 << (uint64_t)shift);
            }

            uint8_t *parent0 = GetNode(g, begin + (level - 1) * perLevel + prev);
            uint8_t *parent1 = GetNode(g, *count - perLevel);
            uint8_t *buf = (uint8_t *)malloc(NODE_SIZE);
            WriteVarInt(buf, *count);
            uint8_t *hashInput = (uint8_t *)malloc(NODE_SIZE * 4);
            memcpy(hashInput, g->pk, NODE_SIZE);
            memcpy(hashInput + NODE_SIZE, buf, NODE_SIZE);
            memcpy(hashInput + (NODE_SIZE * 2), parent0, NODE_SIZE);
            memcpy(hashInput + (NODE_SIZE * 3), parent1, NODE_SIZE);

            uint8_t *hashOutput = (uint8_t *)malloc(NODE_SIZE);
            sha3(hashInput, NODE_SIZE * 4, hashOutput, NODE_SIZE);

            NewNode(g, *count, hashOutput);
            (*count)++;

            free(hashOutput);
            free(hashInput);
            free(parent0);
            free(parent1);
            free(buf);
        }
    }
}

void XiGraphIter(struct Graph *g, int64_t index)
{
    int64_t count = g->pow2;

    int8_t stackSize = 5;
    int64_t *stack = (int64_t *)malloc(sizeof(int64_t) * stackSize);
    for (int i = 0; i < 5; i++)
        stack[i] = index;

    int8_t graphStackSize = 5;
    int32_t *graphStack = (int32_t *)malloc(sizeof(int32_t) * graphStackSize);
    for (int i = 0; i < 5; i++)
        graphStack[i] = graphStackSize - i - 1;

    int64_t i = 0;
    int64_t graph = 0;
    int64_t pow2index = 1 << ((uint64_t)index);

    for (i = 0; i < pow2index; i++)
    {
        uint8_t *buf = (uint8_t *)malloc(NODE_SIZE);
        WriteVarInt(buf, count);
        uint8_t *hashInput = (uint8_t *)malloc(NODE_SIZE * 2);
        memcpy(hashInput, g->pk, NODE_SIZE);
        memcpy(hashInput + NODE_SIZE, buf, NODE_SIZE);
        uint8_t *hashOutput = (uint8_t *)malloc(NODE_SIZE);

        sha3(hashInput, NODE_SIZE * 2, hashOutput, NODE_SIZE);
        NewNode(g, count, hashOutput);
        count++;

        free(hashOutput);
        free(hashInput);
        free(buf);
    }

    if (index == 1)
    {
        ButterflyGraph(g, index, &count);
        return;
    }

    while (stackSize != 0 && graphStackSize != 0)
    {

        index = stack[stackSize - 1];
        graph = graphStack[graphStackSize - 1];

        stackSize--;
        if (stackSize > 0)
        {
            int64_t *tempStack = (int64_t *)malloc(sizeof(int64_t) * (stackSize));
            memcpy(tempStack, stack, sizeof(int64_t) * (stackSize));
            free(stack);
            stack = tempStack;
        }

        graphStackSize--;
        if (graphStackSize > 0)
        {
            int32_t *tempGraphStack = (int32_t *)malloc(sizeof(int32_t) * (graphStackSize));
            memcpy(tempGraphStack, graphStack, sizeof(int32_t) * (graphStackSize));
            free(graphStack);
            graphStack = tempGraphStack;
        }

        int8_t indicesSize = 5;
        int64_t *indices = (int64_t *)malloc(sizeof(int64_t) * indicesSize);
        for (int i = 0; i < indicesSize; i++)
            indices[i] = index - 1;

        int8_t graphsSize = 5;
        int32_t *graphs = (int32_t *)malloc(sizeof(int32_t) * graphsSize);
        for (int i = 0; i < graphsSize; i++)
            graphs[i] = graphsSize - i - 1;

        int64_t pow2indexInner = 1 << ((uint64_t)index);
        int64_t pow2indexInner_1 = 1 << ((uint64_t)index - 1);

        if (graph == 0)
        {
            uint64_t sources = count - pow2indexInner;
            for (i = 0; i < pow2indexInner_1; i++)
            {
                uint8_t *parent0 = GetNode(g, sources + i);
                uint8_t *parent1 = GetNode(g, sources + i + pow2indexInner_1);

                uint8_t *buf = (uint8_t *)malloc(NODE_SIZE);
                WriteVarInt(buf, count);

                uint8_t *hashInput = (uint8_t *)malloc(NODE_SIZE * 4);
                memcpy(hashInput, g->pk, NODE_SIZE);
                memcpy(hashInput + NODE_SIZE, buf, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 2), parent0, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 3), parent1, NODE_SIZE);

                uint8_t *hashOutput = (uint8_t *)malloc(NODE_SIZE);
                sha3(hashInput, NODE_SIZE * 4, hashOutput, NODE_SIZE);

                NewNode(g, count, hashOutput);
                count++;

                free(hashOutput);
                free(hashInput);
                free(parent0);
                free(parent1);
                free(buf);
            }
        }
        else if (graph == 1)
        {
            uint64_t firstXi = count;
            for (i = 0; i < pow2indexInner_1; i++)
            {
                uint64_t nodeId = firstXi + i;
                uint8_t *parent = GetNode(g, firstXi - pow2indexInner_1 + i);

                uint8_t *buf = (uint8_t *)malloc(NODE_SIZE);
                WriteVarInt(buf, nodeId);

                uint8_t *hashInput = (uint8_t *)malloc(NODE_SIZE * 3);
                memcpy(hashInput, g->pk, NODE_SIZE);
                memcpy(hashInput + NODE_SIZE, buf, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 2), parent, NODE_SIZE);

                uint8_t *hashOutput = (uint8_t *)malloc(NODE_SIZE);
                sha3(hashInput, NODE_SIZE * 3, hashOutput, NODE_SIZE);

                NewNode(g, count, hashOutput);
                count++;

                free(hashOutput);
                free(hashInput);
                free(parent);
                free(buf);
            }
        }
        else if (graph == 2)
        {
            uint64_t secondXi = count;
            for (i = 0; i < pow2indexInner_1; i++)
            {
                uint64_t nodeId = secondXi + i;
                uint8_t *parent = GetNode(g, secondXi - pow2indexInner_1 + i);

                uint8_t *buf = (uint8_t *)malloc(NODE_SIZE);
                WriteVarInt(buf, nodeId);

                uint8_t *hashInput = (uint8_t *)malloc(NODE_SIZE * 3);
                memcpy(hashInput, g->pk, NODE_SIZE);
                memcpy(hashInput + NODE_SIZE, buf, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 2), parent, NODE_SIZE);

                uint8_t *hashOutput = (uint8_t *)malloc(NODE_SIZE);
                sha3(hashInput, NODE_SIZE * 3, hashOutput, NODE_SIZE);

                NewNode(g, count, hashOutput);
                count++;

                free(hashOutput);
                free(hashInput);
                free(parent);
                free(buf);
            }
        }
        else if (graph == 3)
        {
            uint64_t secondButter = count;
            for (i = 0; i < pow2indexInner_1; i++)
            {
                uint64_t nodeId = secondButter + i;
                uint8_t *parent = GetNode(g, secondButter - pow2indexInner_1 + i);

                uint8_t *buf = (uint8_t *)malloc(NODE_SIZE);
                WriteVarInt(buf, nodeId);

                uint8_t *hashInput = (uint8_t *)malloc(NODE_SIZE * 3);
                memcpy(hashInput, g->pk, NODE_SIZE);
                memcpy(hashInput + NODE_SIZE, buf, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 2), parent, NODE_SIZE);

                uint8_t *hashOutput = (uint8_t *)malloc(NODE_SIZE);
                sha3(hashInput, NODE_SIZE * 3, hashOutput, NODE_SIZE);

                NewNode(g, count, hashOutput);
                count++;

                free(hashOutput);
                free(hashInput);
                free(parent);
                free(buf);
            }
        }
        else
        {
            uint64_t sinks = count;
            uint64_t sources = sinks + pow2indexInner - numXi(index);
            for (i = 0; i < pow2indexInner_1; i++)
            {
                uint64_t nodeId0 = sinks + i;
                uint64_t nodeId1 = sinks + i + pow2indexInner_1;
                uint8_t *parent0 = GetNode(g, sinks - pow2indexInner_1 + i);
                uint8_t *parent1_0 = GetNode(g, sources + i);
                uint8_t *parent1_1 = GetNode(g, sources + i + pow2indexInner_1);

                uint8_t *buf = (uint8_t *)malloc(NODE_SIZE);
                WriteVarInt(buf, nodeId0);

                uint8_t *hashInput = (uint8_t *)malloc(NODE_SIZE * 4);
                memcpy(hashInput, g->pk, NODE_SIZE);
                memcpy(hashInput + NODE_SIZE, buf, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 2), parent0, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 3), parent1_0, NODE_SIZE);

                uint8_t *hashOutput0 = (uint8_t *)malloc(NODE_SIZE);
                sha3(hashInput, NODE_SIZE * 4, hashOutput0, NODE_SIZE);

                WriteVarInt(buf, nodeId1);

                memcpy(hashInput, g->pk, NODE_SIZE);
                memcpy(hashInput + NODE_SIZE, buf, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 2), parent0, NODE_SIZE);
                memcpy(hashInput + (NODE_SIZE * 3), parent1_1, NODE_SIZE);

                uint8_t *hashOutput1 = (uint8_t *)malloc(NODE_SIZE);
                sha3(hashInput, NODE_SIZE * 4, hashOutput1, NODE_SIZE);

                NewNode(g, nodeId0, hashOutput0);
                NewNode(g, nodeId1, hashOutput1);
                count += 2;

                free(parent0);
                free(parent1_0);
                free(parent1_1);
                free(buf);
                free(hashInput);
                free(hashOutput0);
                free(hashOutput1);
            }
        }

        if ((graph == 0 || graph == 3) ||
            ((graph == 1 || graph == 2) && index == 2))
        {
            ButterflyGraph(g, index - 1, &count);
        }
        else if (graph == 1 || graph == 2)
        {

            int64_t *tempStack = (int64_t *)malloc(sizeof(int64_t) * (stackSize + indicesSize));
            memcpy(tempStack, stack, stackSize * sizeof(int64_t));
            memcpy(tempStack + stackSize, indices, indicesSize * sizeof(int64_t));
            stackSize += indicesSize;
            free(stack);
            stack = tempStack;

            int32_t *tempGraphStack = (int32_t *)malloc(sizeof(int32_t) * (graphStackSize + graphsSize));
            memcpy(tempGraphStack, graphStack, graphStackSize * sizeof(int32_t));
            memcpy(tempGraphStack + graphStackSize, graphs, graphsSize * sizeof(int32_t));
            graphStackSize += graphsSize;
            free(graphStack);
            graphStack = tempGraphStack;
        }

        free(indices);
        free(graphs);
    }

    free(stack);
    free(graphStack);
}

struct Graph *NewGraph(int64_t index, const char *targetFile, uint8_t *pk)
{
    uint8_t exists = 0;
    FILE *db;
    if ((db = fopen(targetFile, "r")) != NULL)
    {
        fclose(db);
        exists = 1;
    }

    db = fopen(targetFile, "wb+");
    int64_t size = numXi(index);
    int64_t log2 = Log2(size) + 1;
    int64_t pow2 = 1 << ((uint64_t)log2);

    struct Graph *g = (struct Graph *)malloc(sizeof(struct Graph));
    g->db = db;
    g->log2 = log2;
    g->pow2 = pow2;
    g->pk = pk;
    g->index = index;

    if (exists == 0)
    {
        XiGraphIter(g, index);
    }

    fclose(db);
    return g;
}

////////////////////////////
// Hashing

static inline uint32_t fnv1a(const uint32_t a, const uint32_t b) {
    return (a ^ b) * 0x1000193;
}

int verthash(const unsigned char* input, const size_t input_size, unsigned char* output) {
    if(!blob_initialized)
        return 0;

    unsigned char p1[HASH_OUT_SIZE];
    sha3(&input[0], input_size, &p1[0], HASH_OUT_SIZE);

    unsigned char p0[N_SUBSET];

#ifndef _MSC_VER
    unsigned char input_header[input_size];
#else
    unsigned char* input_header = _alloca(input_size);
#endif // !MSVC

    memcpy(input_header, input, input_size);

    for(size_t i = 0; i < N_ITER; i++) {
    	input_header[0] += 1;
    	sha3(&input_header[0], input_size, p0+i*P0_SIZE, P0_SIZE);
    }

    uint32_t* p0_index = (uint32_t*)p0;
    uint32_t seek_indexes[N_INDEXES];

    for(size_t x = 0; x < N_ROT; x++) {
    	memcpy(seek_indexes+x*(N_SUBSET/sizeof(uint32_t)), p0, N_SUBSET);
    	for(size_t y = 0; y < N_SUBSET/sizeof(uint32_t); y++) {
	        *(p0_index + y) = (*(p0_index + y) << 1) | (1 & (*(p0_index + y) >> 31));
	    }
    }

    uint32_t* p1_32 = (uint32_t*)p1;
    uint32_t* blob_bytes_32 = (uint32_t*)blob_bytes;
    uint32_t value_accumulator = 0x811c9dc5;
    const uint32_t mdiv = ((blob_size - HASH_OUT_SIZE)/BYTE_ALIGNMENT) + 1;
    for(size_t i = 0; i < N_INDEXES; i++) {
        const uint32_t offset = (fnv1a(seek_indexes[i], value_accumulator) % mdiv) * BYTE_ALIGNMENT/sizeof(uint32_t);
        for(size_t i2 = 0; i2 < HASH_OUT_SIZE/sizeof(uint32_t); i2++) {
            const uint32_t value = *(blob_bytes_32 + offset + i2);
            uint32_t* p1_ptr = p1_32 + i2;
            *p1_ptr = fnv1a(*p1_ptr, value);

            value_accumulator = fnv1a(value_accumulator, value);
        }
    }

    memcpy(output, p1, HASH_OUT_SIZE);

    return 1;
}

int verthash_init(const char* dat_file_name, int createIfMissing) {
    if(blob_initialized == 1) return 0;

    FILE* datfile = fopen(dat_file_name, "rb");

    if(datfile == NULL) {
        if(createIfMissing) {
            // create if missing
            static const char *hashInput = "Verthash Proof-of-Space Datafile";
            uint8_t *pk = (uint8_t *)malloc(NODE_SIZE);
            sha3(hashInput, 32, pk, NODE_SIZE);

            int64_t index = 17;
            NewGraph(index, dat_file_name, pk);

            // open
            datfile = fopen(dat_file_name, "rb");
        } else {
            return -1;
        }
    }

    fseek(datfile, 0, SEEK_END);
    blob_size = ftell(datfile);

    fseek(datfile, 0, SEEK_SET);

    blob_bytes = malloc(blob_size);

    const size_t bytes_read = fread(blob_bytes, 1, blob_size, datfile);
    if(bytes_read != blob_size) {
        return -1;
    }

    fclose(datfile);

    blob_initialized = 1;
    return 0;
}
